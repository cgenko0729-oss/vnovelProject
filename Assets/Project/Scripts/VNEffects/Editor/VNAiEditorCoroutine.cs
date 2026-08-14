using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VNEffectsEditor
{
    /// <summary>
    /// 在 Play Mode 之外驱动协程的泵。
    ///
    /// 为什么需要：`VNAiClient.Send` 是协程（UnityWebRequest），而编辑器里没有
    /// MonoBehaviour.StartCoroutine。挂 EditorApplication.update 手动 MoveNext 即可，
    /// 于是 AI 那一整套逻辑不用进 Play Mode 就能跑（自检菜单与试聊台窗口共用这一份）。
    ///
    /// 需要支持的 yield 类型：null / AsyncOperation（SendWebRequest）/
    /// CustomYieldInstruction（WaitForSecondsRealtime）/ 嵌套 IEnumerator。
    ///
    /// ★ 踩过的坑：子协程跑完弹栈后，父协程的 Current 仍然指着那个**已耗尽的**
    ///   子协程对象。只判断「Current 是不是 IEnumerator」会把它无限重新压栈，
    ///   父协程永远前进不了一步，表现为「点了菜单没反应也不报错」。
    ///   所以用 _started 记下每个已经驱动过的子协程，只压栈一次。
    ///
    /// ★ 域重载（改代码 / 进 Play Mode）会清掉 EditorApplication.update 的委托，
    ///   协程随之无声死亡。调用方要自己判断「我以为在跑但泵已经没了」——
    ///   窗口那边靠 IsRunning 为 false 但状态还停在「等待回复」来识别并标成已中断。
    /// </summary>
    public class VNAiEditorCoroutine
    {
        readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
        readonly HashSet<IEnumerator> _started = new HashSet<IEnumerator>();
        readonly Action _onFinished;
        EditorApplication.CallbackFunction _tick;

        /// <summary>还在跑（没跑完、也没被 Stop）</summary>
        public bool IsRunning { get; private set; }

        /// <summary>协程内部抛出的异常；null = 没炸</summary>
        public Exception Error { get; private set; }

        /// <param name="onFinished">正常跑完或抛异常后回调一次；Stop() 不触发</param>
        public static VNAiEditorCoroutine Start(IEnumerator routine, Action onFinished = null)
            => new VNAiEditorCoroutine(routine, onFinished);

        VNAiEditorCoroutine(IEnumerator routine, Action onFinished)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            _onFinished = onFinished;
            _stack.Push(routine);
            _started.Add(routine);
            IsRunning = true;

            _tick = Tick;
            EditorApplication.update += _tick;
        }

        /// <summary>提前中止（窗口关闭、玩家点了取消）。不会触发 onFinished。</summary>
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            EditorApplication.update -= _tick;
        }

        void Tick()
        {
            if (_stack.Count == 0) { Finish(); return; }

            var top = _stack.Peek();
            object cur = top.Current;

            // 还没等完就直接返回，下一帧再看
            if (cur is AsyncOperation ao && !ao.isDone) return;
            if (cur is CustomYieldInstruction cy && cy.keepWaiting) return;
            if (cur is IEnumerator nested && _started.Add(nested))
            {
                _stack.Push(nested);
                return;
            }

            bool alive;
            try { alive = top.MoveNext(); }
            catch (Exception e)
            {
                Error = e;
                Debug.LogError($"[VNAi] 编辑器协程抛异常：{e}");
                Finish();
                return;
            }

            if (!alive)
            {
                _stack.Pop();
                if (_stack.Count == 0) Finish();
            }
        }

        void Finish()
        {
            if (!IsRunning) return;
            IsRunning = false;
            EditorApplication.update -= _tick;
            _onFinished?.Invoke();
        }
    }
}
