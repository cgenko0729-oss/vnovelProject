using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNPhotoBoothModule
    {
        // ==================================================================
        // 装扮
        // ==================================================================

        void ApplyFrame(VNPhotoFrameDef def)
        {
            _frame = def;

            // 底图
            if (def != null)
            {
                _frameBack.sprite = def.ResolveFrameSprite();
                _frameBack.color = Color.white;
            }
            else
            {
                _frameBack.sprite = null;
                _frameBack.color = new Color(0.97f, 0.97f, 0.98f, 1f);
            }

            // 开窗
            var maskShape = def != null ? def.maskShape : VNPhotoMaskShape.None;
            Vector2 maskSize = def != null ? def.maskSize : Vector2.one;
            Vector2 maskOffset = def != null ? def.maskOffset : Vector2.zero;
            var windowSize = new Vector2(ViewW * Mathf.Clamp01(maskSize.x),
                                         ViewH * Mathf.Clamp01(maskSize.y));
            var windowPos = new Vector2(ViewW * maskOffset.x, ViewH * maskOffset.y);

            _windowImage.sprite = VNPhotoTextures.MaskSprite(maskShape);
            _windowImage.color = def != null ? def.windowColor : new Color(0.87f, 0.94f, 1f, 1f);
            VNPhotoBoothUi.Center(_window, windowSize, windowPos);

            float edge = def != null ? def.maskEdgeWidth : 0f;
            var ringColor = def != null ? def.maskEdgeColor : new Color(0f, 0f, 0f, 0f);
            _windowRing.sprite = VNPhotoTextures.MaskRingSprite(maskShape);
            _windowRing.color = ringColor;
            _windowRing.enabled = ringColor.a > 0.01f && edge > 0f;

            ApplyBackdrop(_backdrop);   // 开窗尺寸变了，背景要重新按 cover 铺一次

            // 人后贴纸层要抵消开窗偏移，才能与人前贴纸层共用同一套坐标
            //（否则边框换成偏心开窗时，翻层的贴纸会整体跳位）
            if (_backStickerLayer != null)
                _backStickerLayer.anchoredPosition = -windowPos;

            // 人物能被拖多远，跟着开窗大小走。
            // ★ x 必须给到 0.75W：基准站位在 ±0.275W，要让左边那个能越过中线走到
            //   右边去（换位），单侧行程至少得有 0.55W，再留点余量才够用。
            if (_dragger != null)
                _dragger.bounds = new Vector2(windowSize.x * 0.75f, windowSize.y * 0.55f);
            VNPhotoBoothUi.Center((RectTransform)_windowRing.transform,
                windowSize + Vector2.one * edge, windowPos);

            // 前景与水印
            var front = def != null ? def.frontSprite : null;
            _frameFront.sprite = front;
            _frameFront.enabled = front != null;

            string mark = def != null ? def.DisplayWatermark : null;
            _watermark.text = mark ?? "";
            _watermark.color = def != null ? def.watermarkColor : Color.white;
            _watermark.gameObject.SetActive(!string.IsNullOrEmpty(mark));

            RebuildFrameDecorations();
            RefreshPortraits();
        }

        /// <summary>
        /// 换背景。图按 **cover** 铺满开窗（宁可裁掉两侧也不留白边、不拉变形）——
        /// 背景是「照相馆的背景布」，露白比裁切难看得多。
        /// </summary>
        void ApplyBackdrop(VNPhotoBackdropDef def)
        {
            _backdrop = def;
            if (_backdropImage == null) return;

            var sprite = def != null ? def.ResolveSprite() : null;
            if (sprite == null)
            {
                _backdropImage.enabled = false;   // 没选背景 = 露出边框的开窗底色
                return;
            }

            _backdropImage.enabled = true;
            _backdropImage.sprite = sprite;
            _backdropImage.preserveAspect = false;

            Vector2 window = _window != null ? _window.sizeDelta : new Vector2(ViewW, ViewH);
            float winAspect = window.y > 0f ? window.x / window.y : 1f;
            float imgAspect = sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height : winAspect;

            // cover 基准：刚好铺满开窗的尺寸（玩家的缩放倍率从这里往上乘）
            Vector2 cover = imgAspect > winAspect
                ? new Vector2(window.y * imgAspect, window.y)   // 图更宽 → 以高为准，裁两侧
                : new Vector2(window.x, window.x / imgAspect);  // 图更高 → 以宽为准，裁上下

            float scale = _dragger != null ? Mathf.Max(1f, _dragger.backdropScale) : 1f;
            Vector2 size = cover * scale;

            // 可移动范围 = 溢出开窗的那一半。scale=1 时某个方向恰好是 0，
            // 于是「不允许露边」这条规则天然由这个钳制保证，不需要额外判断。
            var slack = new Vector2(
                Mathf.Max(0f, (size.x - window.x) * 0.5f),
                Mathf.Max(0f, (size.y - window.y) * 0.5f));

            Vector2 offset = _dragger != null ? _dragger.backdropOffset : Vector2.zero;
            offset = new Vector2(
                Mathf.Clamp(offset.x, -slack.x, slack.x),
                Mathf.Clamp(offset.y, -slack.y, slack.y));
            if (_dragger != null) _dragger.backdropOffset = offset;   // 钳制后写回

            VNPhotoBoothUi.Center((RectTransform)_backdropImage.transform, size, offset);
        }

        /// <summary>边框自带装饰件：换边框时整批重建（它们属于边框，不计分）</summary>
        void RebuildFrameDecorations()
        {
            foreach (var go in _frameDecorations) if (go != null) Destroy(go);
            _frameDecorations.Clear();

            if (_frame == null || _frame.decorations == null) return;
            foreach (var deco in _frame.decorations)
            {
                if (deco == null || deco.sticker == null) continue;
                var item = SpawnSticker(deco.sticker, deco.position, deco.scale,
                    deco.rotation, true);
                if (item == null) continue;
                item.locked = deco.locked;
                _frameDecorations.Add(item.gameObject);
            }
        }

        VNPhotoStickerItem SpawnSticker(VNPhotoStickerDef def, Vector2 position, float scale,
            float rotation, bool fromFrame)
        {
            if (def == null) return null;

            var image = VNPhotoBoothUi.CreateImage($"Sticker_{def.stickerId}", _stickerLayer,
                def.ResolveSprite(), def.tint, true);
            image.preserveAspect = true;
            var rect = (RectTransform)image.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * def.defaultSize;

            var item = image.gameObject.AddComponent<VNPhotoStickerItem>();
            item.stickerId = def.stickerId;
            item.bounds = new Vector2(ViewW * 0.5f, ViewH * 0.5f);
            item.Place(position, scale, rotation);
            item.onDelete = RemoveSticker;
            item.onToggleLayer = ToggleStickerLayer;

            if (!fromFrame) _playerStickers.Add(item);
            return item;
        }

        /// <summary>
        /// 双击贴纸：在「人前」与「人后」之间翻转。
        /// 两个贴纸层尺寸都等于取景框，坐标系一致，所以换父节点后位置原样不动
        /// （人后那层在 Mask 里，超出开窗的部分会被裁掉——它已经在照片"里面"了）。
        /// </summary>
        void ToggleStickerLayer(VNPhotoStickerItem item)
        {
            if (_phase != Phase.Dressing || item == null || item.locked) return;
            if (_stickerLayer == null || _backStickerLayer == null) return;

            bool inFront = item.transform.parent == _stickerLayer;
            item.transform.SetParent(inFront ? _backStickerLayer : _stickerLayer, false);
            item.transform.SetAsLastSibling();
            _sfx?.Play(VNPhotoSfx.Kind.Place, inFront ? 0.8f : 1.15f);
        }

        /// <summary>双击某个人：把他提到另一个人前面</summary>
        void BringPortraitToFront(bool isMe)
        {
            if (_phase != Phase.Dressing) return;
            var target = isMe ? _meImage : _herImage;
            if (target == null || !target.enabled) return;
            target.transform.SetAsLastSibling();
            _sfx?.Play(VNPhotoSfx.Kind.Place, isMe ? 1.1f : 0.9f);
        }

        void RemoveSticker(VNPhotoStickerItem item)
        {
            if (item == null || _phase != Phase.Dressing) return;
            _playerStickers.Remove(item);
            _frameDecorations.Remove(item.gameObject);
            Destroy(item.gameObject);
        }

        Vector2 RandomSpawnPos()
        {
            // 落在取景框内偏外圈的位置，别一上来就糊住脸
            float x = Random.Range(0.24f, 0.42f) * ViewW * (Random.value < 0.5f ? -1f : 1f);
            float y = Random.Range(-0.34f, 0.34f) * ViewH;
            return new Vector2(x, y);
        }

        void RefreshPortraits()
        {
            // 两人各分半个开窗的宽度；单人时对方居中占满
            float windowWidth = _window != null ? _window.sizeDelta.x : ViewW;
            bool solo = _meDef == null;
            float slotWidth = solo ? windowWidth : windowWidth * 0.5f;
            float half = slotWidth * pairSpread * 0.5f;   // 肩膀轻微交叠是合影该有的样子

            // 玩家拖出来的偏移叠在基准站位上（切表情重摆时不会被冲掉）
            Vector2 meDrag = _dragger != null ? _dragger.meOffset : Vector2.zero;
            Vector2 herDrag = _dragger != null ? _dragger.herOffset : Vector2.zero;

            // 玩家滚轮缩出来的倍率叠在取景倍率上——素材尺寸不统一时靠它救场
            float meScale = _dragger != null ? _dragger.meScale : 1f;
            float herScale = _dragger != null ? _dragger.herScale : 1f;

            float meRot = _dragger != null ? _dragger.meRotation : 0f;
            float herRot = _dragger != null ? _dragger.herRotation : 0f;

            VNPhotoBoothUi.ApplyPortrait(_meImage, _meDef, _meExpr, slotWidth,
                FitFor(_meDef) * meScale, AnchorFor(_meDef),
                new Vector2(-half, 0f) + meDrag, mirrorMe, meRot);
            VNPhotoBoothUi.ApplyPortrait(_herImage, _herDef, _herExpr, slotWidth,
                FitFor(_herDef) * herScale, AnchorFor(_herDef),
                new Vector2(solo ? 0f : half, 0f) + herDrag, false, herRot);
        }

        PortraitTweak TweakFor(VNCharacterDef def)
        {
            if (def == null || portraitTweaks == null) return null;
            foreach (var t in portraitTweaks)
                if (t != null && t.characterId == def.id) return t;
            return null;
        }

        float FitFor(VNCharacterDef def)
        {
            var tweak = TweakFor(def);
            return tweak != null && tweak.fit > 0f ? tweak.fit : photoFit;
        }

        float AnchorFor(VNCharacterDef def)
        {
            var tweak = TweakFor(def);
            return tweak != null && tweak.faceAnchor >= 0f ? tweak.faceAnchor : faceAnchor;
        }

        void RefreshFrameCells()
        {
            for (int i = 0; i < _frameCells.Count; i++)
            {
                if (_frameCells[i] == null) continue;
                bool selected = i == 0 ? _frame == null
                    : i - 1 < frames.Count && frames[i - 1] == _frame;
                _frameCells[i].color = selected
                    ? VNPhotoBoothUi.CellSelected : VNPhotoBoothUi.CellBg;
            }
        }

        void RefreshExpressionCells()
        {
            HighlightCells(_meCells, _meDef, _meExpr);
            HighlightCells(_herCells, _herDef, _herExpr);
        }

        void HighlightCells(List<Image> cells, VNCharacterDef def, string current)
        {
            if (def?.expressions == null) return;
            for (int i = 0; i < cells.Count && i < def.expressions.Count; i++)
            {
                if (cells[i] == null) continue;
                cells[i].color = def.expressions[i].name == current
                    ? VNPhotoBoothUi.CellSelected : VNPhotoBoothUi.CellBg;
            }
        }
    }
}
