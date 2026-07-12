/// <summary>
/// 再生モード連動で Game View をネイティブ全画面表示する Editor 拡張
/// @file GameViewFullscreen.cs
/// @author g-cho
/// @date 2026/07/05
/// </summary>
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FrameworkSample {

	/// <summary>
	/// Game View をタイトルバー無しの ContainerWindow へ移し替えて全画面表示する Editor 拡張。
	/// 再生開始で自動全画面化、再生終了で自動復帰する。P キー（Tools > Fullscreen Game View）で手動切替も可能。
	///
	/// 仕組み（Fullscreen Editor プラグインの方式を簡略化して移植）:
	/// 1. 装飾無し（ShowMode.PopupMenu）の ContainerWindow + HostView を生成し、プレースホルダを載せて表示する
	/// 2. 元の Game View とプレースホルダの actualView を入れ替える（元のドック位置にはプレースホルダが残る）
	/// 3. 復帰時は逆に入れ替えてから ContainerWindow ごと破棄する
	///
	/// 制限事項:
	/// - UnityEditor.GameView / ContainerWindow / HostView は internal API のためリフレクション経由で操作する
	///   （Unity のメジャーバージョン更新で内部名が変わると動作しなくなる可能性がある）
	/// - Windows Editor のみ動作確認対象（macOS / Linux は未対応）
	/// - Editor 専用機能でありビルド後の Player には一切影響しない
	/// </summary>
	[InitializeOnLoad]
	public static class GameViewFullscreen {

		private const string _MENU_PATH = "Tools/Fullscreen Game View _p";

		// UnityEditor.ShowMode の値（internal enum のため定数で保持する）
		private const int _SHOW_MODE_POPUP_MENU = 1;
		private const int _SHOW_MODE_NO_SHADOW = 3;

		// ツールバー高さの取得に失敗した場合の既定値
		private const float _DEFAULT_TOOLBAR_HEIGHT = 21f;

		private const BindingFlags _ANY_BINDING = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

		private static readonly Type s_GameViewType = _FindEditorType( "UnityEditor.GameView" );

		private static readonly Type s_HostViewType = _FindEditorType( "UnityEditor.HostView" );

		private static readonly Type s_ContainerWindowType = _FindEditorType( "UnityEditor.ContainerWindow" );

		static GameViewFullscreen() {
			EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
			EditorApplication.update += _WatchExternalClose;
			EditorApplication.wantsToQuit += _OnWantsToQuit;
		}

		/// <summary>全画面表示中かどうか。</summary>
		public static bool IsOpen {
			get { return _FindState() != null; }
		}

		/// <summary>全画面表示を切り替える。</summary>
		public static void Toggle() {
			if( IsOpen ) {
				_Close();
			} else {
				_Open();
			}
		}

		[MenuItem( _MENU_PATH, false, 0 )]
		private static void _ToggleMenuItem() {
			Toggle();
		}

		[MenuItem( _MENU_PATH, true )]
		private static bool _ToggleMenuItemValidate() {
			Menu.SetChecked( _MENU_PATH, IsOpen );
			return true;
		}

		private static void _OnPlayModeStateChanged( PlayModeStateChange state ) {
			switch( state ) {
			case PlayModeStateChange.EnteredPlayMode:
				// ドメインリロード完了後に全画面化する（リロード前に開くと状態管理が複雑になるため）
				if( !IsOpen ) {
					_Open();
				}
				break;

			case PlayModeStateChange.ExitingPlayMode:
				if( IsOpen ) {
					_Close();
				}
				break;
			}
		}

		/// <summary>全画面ウィンドウまたはプレースホルダが外部要因（Alt+F4 やタブ閉じ）で破棄された場合の後始末。</summary>
		private static void _WatchExternalClose() {
			var state = _FindState();
			if( state == null ) {
				return;
			}

			if( state.container == null || state.placeholder == null ) {
				_Close();
			}
		}

		private static bool _OnWantsToQuit() {
			// 終了前に必ず復帰させ、壊れたレイアウトが保存されるのを防ぐ
			if( IsOpen ) {
				_Close();
			}
			return true;
		}

		private static void _Open() {
			if( s_GameViewType == null || s_HostViewType == null || s_ContainerWindowType == null ) {
				Debug.LogError( "[GameViewFullscreen] UnityEditor 内部型の取得に失敗しました。Unity バージョン更新の影響の可能性があります。" );
				return;
			}

			var game_view = _FindGameView();
			if( game_view == null ) {
				// Game View が一つも無い場合は生成する
				game_view = EditorWindow.GetWindow( s_GameViewType );
			}

			var rect = _GetFullscreenRect( game_view );

			// 全画面用の ContainerWindow + HostView を生成し、まずプレースホルダを載せる
			var placeholder = ScriptableObject.CreateInstance<GameViewFullscreenPlaceholder>();
			placeholder.titleContent = game_view.titleContent;

			var host_view = ScriptableObject.CreateInstance( s_HostViewType );
			var container = ScriptableObject.CreateInstance( s_ContainerWindowType );

			_SetProperty( host_view, "actualView", placeholder );

			// 順序が重要: position → rootView → Show → SetMinMaxSizes の順でないと初回リサイズまで位置が反映されない
			_SetProperty( container, "position", rect );
			_SetProperty( container, "rootView", host_view );
			_Invoke( placeholder, "MakeParentsSettingsMatchMe" );

			_ShowContainer( container, rect );

			// ウィンドウ装飾（タイトルバー・影）を描画させない
			_SetField( container, "m_ShowMode", _SHOW_MODE_POPUP_MENU );
			// レイアウトファイルへ保存させない（次回起動時のレイアウト破損を防ぐ）
			_SetField( container, "m_DontSaveToLayout", true );

			// 元の Game View と全画面側のプレースホルダを入れ替える
			_SwapWindows( game_view, placeholder );

			// Game View 上部のツールバーを画面外へクリップして映像のみ表示する
			var toolbar_height = _GetToolbarHeight();
			_Invoke( host_view, "SetPosition", new Rect( 0f, -toolbar_height, rect.width, rect.height + toolbar_height ) );

			// ドメインリロードを跨いで参照を保持する状態オブジェクト
			var state = ScriptableObject.CreateInstance<GameViewFullscreenState>();
			state.hideFlags = HideFlags.HideAndDontSave;
			state.game_view = game_view;
			state.placeholder = placeholder;
			state.container = container;

			game_view.Focus();
		}

		private static void _Close() {
			var state = _FindState();
			if( state == null ) {
				return;
			}

			var game_view = state.game_view;
			var placeholder = state.placeholder;
			var container = state.container;

			// 先に状態を破棄して _WatchExternalClose からの再入を防ぐ
			UnityEngine.Object.DestroyImmediate( state );

			if( game_view != null && placeholder != null && container != null ) {
				// Game View を元のドック位置へ戻し、プレースホルダごと全画面ウィンドウを破棄する
				_SwapWindows( game_view, placeholder );
				_Invoke( container, "Close" );
				game_view.Focus();
			} else {
				// 異常系: 全画面ウィンドウやプレースホルダが先に破棄されていた場合は残骸を後始末する
				if( container != null ) {
					_Invoke( container, "Close" );
				}
				if( placeholder != null ) {
					placeholder.Close();
				}
				Debug.LogWarning( "[GameViewFullscreen] 全画面表示が正常に復帰できませんでした。Game View が消えた場合は Window メニューから開き直してください。" );
			}
		}

		/// <summary>2 つの EditorWindow の親 HostView 上の actualView を入れ替える（Fullscreen Editor の SwapWindows 相当）。</summary>
		private static void _SwapWindows( EditorWindow a, EditorWindow b ) {
			var parent_a = (ScriptableObject)_GetField( a, "m_Parent" );
			var parent_b = (ScriptableObject)_GetField( b, "m_Parent" );

			var container_a = _GetProperty( parent_a, "window" );
			var container_b = _GetProperty( parent_b, "window" );

			var selected_a = (EditorWindow)_GetProperty( parent_a, "actualView" );
			var selected_b = (EditorWindow)_GetProperty( parent_b, "actualView" );

			// 入れ替え中の再描画によるチラつきを防ぐ
			_SetFreeze( container_a, true );
			_SetFreeze( container_b, true );

			_SetProperty( parent_a, "actualView", b );
			_SetProperty( parent_b, "actualView", a );

			_ReplaceDockedPane( parent_a, a, b );
			_ReplaceDockedPane( parent_b, b, a );

			_Invoke( a, "MakeParentsSettingsMatchMe" );
			_Invoke( b, "MakeParentsSettingsMatchMe" );

			// 入れ替え対象以外のタブが手前に選択されていた場合はそれを復元する
			if( selected_a != a ) {
				_SetProperty( parent_a, "actualView", selected_a );
			}
			if( selected_b != b ) {
				_SetProperty( parent_b, "actualView", selected_b );
			}

			_SetFreeze( container_a, false );
			_SetFreeze( container_b, false );
		}

		/// <summary>ContainerWindow の再描画を一時停止する（SetFreezeDisplay が存在しないバージョンでは何もしない）。</summary>
		private static void _SetFreeze( object container, bool freeze ) {
			if( container == null ) {
				return;
			}

			var method = container.GetType().GetMethod( "SetFreezeDisplay", _ANY_BINDING, null, new[] { typeof( bool ) }, null );
			if( method != null ) {
				method.Invoke( container, new object[] { freeze } );
			}
		}

		/// <summary>親が DockArea の場合、タブ一覧（m_Panes）内のウィンドウも差し替える。</summary>
		private static void _ReplaceDockedPane( ScriptableObject dock_area, EditorWindow original, EditorWindow replacement ) {
			var panes_field = _FindField( dock_area.GetType(), "m_Panes" );
			if( panes_field == null ) {
				// DockArea ではなく単独 HostView の場合はタブ一覧が無いので何もしない
				return;
			}

			var panes = (List<EditorWindow>)panes_field.GetValue( dock_area );
			var index = panes.IndexOf( original );
			if( index >= 0 ) {
				panes[index] = replacement;
			}
		}

		/// <summary>
		/// ContainerWindow を表示する。
		/// Show の第 1 引数は internal enum の ShowMode（バージョンにより引数の個数も異なる）ため、
		/// 型指定では探せない。名前と引数個数で候補を集め、引数の多いオーバーロードを優先して呼び出す。
		/// 引数順は全バージョン共通で (showMode, loadPosition, displayImmediately[, setFocus[, int]])。
		/// </summary>
		private static void _ShowContainer( ScriptableObject container, Rect rect ) {
			var full_args = new object[] { _SHOW_MODE_NO_SHADOW, false, true, true, 0 };

			MethodInfo show_method = null;
			for( var type = container.GetType(); type != null; type = type.BaseType ) {
				var methods = type.GetMethods( _ANY_BINDING );
				for( var i = 0; i < methods.Length; i++ ) {
					var parameter_count = methods[i].GetParameters().Length;
					if( methods[i].Name != "Show" || parameter_count < 3 || parameter_count > full_args.Length ) {
						continue;
					}
					if( show_method == null || parameter_count > show_method.GetParameters().Length ) {
						show_method = methods[i];
					}
				}
			}

			if( show_method == null ) {
				Debug.LogError( "[GameViewFullscreen] ContainerWindow.Show が見つかりませんでした。Unity バージョン更新の影響の可能性があります。" );
				return;
			}

			var parameters = show_method.GetParameters();
			var args = new object[parameters.Length];
			for( var i = 0; i < parameters.Length; i++ ) {
				// ShowMode 等の enum 引数には int をそのまま渡せないため変換する
				args[i] = parameters[i].ParameterType.IsEnum ? Enum.ToObject( parameters[i].ParameterType, full_args[i] ) : full_args[i];
			}
			show_method.Invoke( container, args );

			// ネイティブウィンドウ生成後にサイズを固定する
			_Invoke( container, "SetMinMaxSizes", rect.size, rect.size );
		}

		/// <summary>Game View が表示されているディスプレイの全域 Rect を取得する。</summary>
		private static Rect _GetFullscreenRect( EditorWindow game_view ) {
			var rect = InternalEditorUtility.GetBoundsOfDesktopAtPoint( game_view.position.center );
			if( rect.width <= 0f || rect.height <= 0f ) {
				// 取得に失敗した場合はフォーカス中ディスプレイの解像度へフォールバックする
				rect = new Rect( 0f, 0f, Screen.currentResolution.width, Screen.currentResolution.height );
			}
			return rect;
		}

		/// <summary>EditorWindow 上部のツールバー高さを取得する（EditorGUI.kWindowToolbarHeight は internal）。</summary>
		private static float _GetToolbarHeight() {
			try {
				var field = _FindField( typeof( EditorGUI ), "kWindowToolbarHeight" );
				if( field != null ) {
					var value = field.GetValue( null );
					if( value is int int_value ) {
						return int_value;
					}
					if( value is float float_value ) {
						return float_value;
					}
					// SVC<float> 型の場合は value プロパティから取得する
					var value_property = value.GetType().GetProperty( "value", _ANY_BINDING );
					if( value_property != null ) {
						return Convert.ToSingle( value_property.GetValue( value, null ) );
					}
				}
			} catch( Exception ) {
				// 取得に失敗しても既定値で継続する
			}
			return _DEFAULT_TOOLBAR_HEIGHT;
		}

		private static EditorWindow _FindGameView() {
			var game_views = Resources.FindObjectsOfTypeAll( s_GameViewType );
			for( var i = 0; i < game_views.Length; i++ ) {
				var window = game_views[i] as EditorWindow;
				if( window != null ) {
					return window;
				}
			}
			return null;
		}

		private static GameViewFullscreenState _FindState() {
			var states = Resources.FindObjectsOfTypeAll<GameViewFullscreenState>();
			return states.Length > 0 ? states[0] : null;
		}

		#region --- リフレクションヘルパー ---
		private static Type _FindEditorType( string full_name ) {
			return typeof( EditorWindow ).Assembly.GetType( full_name );
		}

		/// <summary>private フィールドは基底クラス側に定義されていると GetField で見つからないため、継承階層を遡って探す。</summary>
		private static FieldInfo _FindField( Type type, string name ) {
			while( type != null ) {
				var field = type.GetField( name, _ANY_BINDING );
				if( field != null ) {
					return field;
				}
				type = type.BaseType;
			}
			return null;
		}

		private static PropertyInfo _FindProperty( Type type, string name ) {
			while( type != null ) {
				var property = type.GetProperty( name, _ANY_BINDING );
				if( property != null ) {
					return property;
				}
				type = type.BaseType;
			}
			return null;
		}

		private static object _GetField( object target, string name ) {
			return _FindField( target.GetType(), name ).GetValue( target );
		}

		private static void _SetField( object target, string name, object value ) {
			var field = _FindField( target.GetType(), name );
			if( field.FieldType.IsEnum && value is int int_value ) {
				// enum 型フィールドに int をそのまま渡すと ArgumentException になるため変換する
				value = Enum.ToObject( field.FieldType, int_value );
			}
			field.SetValue( target, value );
		}

		private static object _GetProperty( object target, string name ) {
			return _FindProperty( target.GetType(), name ).GetValue( target, null );
		}

		private static void _SetProperty( object target, string name, object value ) {
			_FindProperty( target.GetType(), name ).SetValue( target, value, null );
		}

		/// <summary>メソッド名と引数個数の一致で internal メソッドを呼び出す。</summary>
		private static object _Invoke( object target, string name, params object[] args ) {
			var type = target.GetType();
			while( type != null ) {
				var methods = type.GetMethods( _ANY_BINDING );
				for( var i = 0; i < methods.Length; i++ ) {
					if( methods[i].Name == name && methods[i].GetParameters().Length == args.Length ) {
						return methods[i].Invoke( target, args );
					}
				}
				type = type.BaseType;
			}
			throw new MissingMethodException( target.GetType().FullName, name );
		}
		#endregion

	}

	/// <summary>
	/// 全画面表示中の参照を保持する状態オブジェクト。
	/// HideAndDontSave によりドメインリロード（再生開始時）を跨いで生存する。
	/// </summary>
	internal class GameViewFullscreenState : ScriptableObject {

		public EditorWindow game_view;

		public EditorWindow placeholder;

		public ScriptableObject container;

	}

	/// <summary>全画面表示中、元の Game View のドック位置を確保しておくプレースホルダウィンドウ。</summary>
	internal class GameViewFullscreenPlaceholder : EditorWindow {

		private void OnGUI() {
			EditorGUILayout.HelpBox( "Game View を全画面表示中です。\nP キーまたは Tools > Fullscreen Game View で復帰します。", MessageType.Info );
		}

	}

}
