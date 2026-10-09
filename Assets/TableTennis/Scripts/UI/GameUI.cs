using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TableTennis
{
    /// <summary>All menus and the in-game HUD, built from code at startup.</summary>
    public class GameUI : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.11f, 0.9f);
        static readonly Color ButtonColor = new Color(0.17f, 0.22f, 0.31f, 1f);
        static readonly Color AccentColor = new Color(1f, 0.56f, 0.12f, 1f);
        static readonly Color TextColor = new Color(0.93f, 0.95f, 0.98f, 1f);
        static readonly Color MutedColor = new Color(0.66f, 0.71f, 0.79f, 1f);
        public static readonly Color GoodColor = new Color(0.45f, 0.9f, 0.5f, 1f);
        public static readonly Color BadColor = new Color(1f, 0.45f, 0.4f, 1f);

        public event Action<AiDifficulty> SinglePlayerRequested;
        public event Action HostRequested;
        public event Action<string> JoinRequested;
        public event Action LeaveRequested;
        public event Action RematchRequested;
        public event Action CopyInviteRequested;

        Font m_Font;
        RectTransform m_Root;

        GameObject m_Menu;
        GameObject m_Lobby;
        GameObject m_Connecting;
        GameObject m_Hud;
        GameObject m_Pause;
        GameObject m_GameOver;

        Text m_MenuStatus;
        InputField m_JoinInput;
        Text m_LobbyCode;
        Text m_LobbyStatus;
        Text m_LobbyHint;
        Button m_CopyButton;
        Text m_ConnectingText;
        Text m_NameLeft;
        Text m_NameRight;
        Text m_ScoreLeft;
        Text m_ScoreRight;
        Text m_Hint;
        Text m_Message;
        Text m_GameOverTitle;
        Text m_GameOverScore;
        Text m_GameOverHint;
        Button m_RematchButton;
        float m_MessageUntil;

        public bool IsPauseOpen => m_Pause != null && m_Pause.activeSelf;

        /// <summary>True while a full-screen menu (not the HUD) is showing.</summary>
        public bool IsMenuOpen => m_Menu.activeSelf || m_Lobby.activeSelf || m_Connecting.activeSelf || m_GameOver.activeSelf || m_Pause.activeSelf;

        void Awake()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();
            BuildMenu();
            BuildLobby();
            BuildConnecting();
            BuildHud();
            BuildPause();
            BuildGameOver();
            ShowMainMenu(string.Empty);
        }

        void Update()
        {
            if (m_Message != null && m_Message.gameObject.activeSelf && Time.unscaledTime > m_MessageUntil)
                m_Message.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ public API

        public void ShowMainMenu(string status, string joinCode = null)
        {
            ShowOnly(m_Menu);
            m_MenuStatus.text = status ?? string.Empty;
            if (!string.IsNullOrEmpty(joinCode))
                m_JoinInput.text = joinCode;
        }

        public void ShowLobby(string code, string hint, bool canCopy)
        {
            ShowOnly(m_Lobby);
            m_LobbyCode.text = code;
            m_LobbyHint.text = hint;
            m_CopyButton.gameObject.SetActive(canCopy);
            SetLobbyStatus("Rakip bekleniyor…");
        }

        public void SetLobbyStatus(string status) => m_LobbyStatus.text = status;

        public void ShowConnecting(string text)
        {
            ShowOnly(m_Connecting);
            m_ConnectingText.text = text;
        }

        public void ShowHud(string leftName, string rightName)
        {
            ShowOnly(m_Hud);
            m_NameLeft.text = leftName;
            m_NameRight.text = rightName;
            m_Message.gameObject.SetActive(false);
        }

        public void SetScore(int left, int right)
        {
            m_ScoreLeft.text = left.ToString();
            m_ScoreRight.text = right.ToString();
        }

        public void SetHint(string hint) => m_Hint.text = hint ?? string.Empty;

        public void ShowMessage(string text, Color color, float seconds = 1.6f)
        {
            m_Message.text = text;
            m_Message.color = color;
            m_Message.gameObject.SetActive(true);
            m_MessageUntil = Time.unscaledTime + seconds;
        }

        public void SetPauseOpen(bool open) => m_Pause.SetActive(open);

        public void ShowGameOver(bool won, string score, bool canRematch, string hint)
        {
            m_GameOver.SetActive(true);
            m_Pause.SetActive(false);
            m_GameOverTitle.text = won ? "KAZANDIN!" : "KAYBETTİN";
            m_GameOverTitle.color = won ? GoodColor : BadColor;
            m_GameOverScore.text = score;
            m_GameOverHint.text = hint;
            m_RematchButton.gameObject.SetActive(canRematch);
        }

        public void HideGameOver() => m_GameOver.SetActive(false);

        public bool IsGameOverVisible => m_GameOver.activeSelf;

        // ------------------------------------------------------------------ construction

        void ShowOnly(GameObject panel)
        {
            m_Menu.SetActive(panel == m_Menu);
            m_Lobby.SetActive(panel == m_Lobby);
            m_Connecting.SetActive(panel == m_Connecting);
            m_Hud.SetActive(panel == m_Hud);
            m_Pause.SetActive(false);
            m_GameOver.SetActive(false);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        void BuildCanvas()
        {
            var go = new GameObject("UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            m_Root = (RectTransform)go.transform;
        }

        void BuildMenu()
        {
            m_Menu = CenterPanel("Main Menu", new Vector2(620, 760));
            var col = m_Menu.transform;

            Label(col, "MASA TENİSİ", 58, AccentColor, FontStyle.Bold, 76);
            Label(col, "Fareyle oynanan masa tenisi", 22, MutedColor, FontStyle.Normal, 34);
            Spacer(col, 14);

            Label(col, "Bilgisayara karşı", 26, TextColor, FontStyle.Bold, 40);
            var row = Row(col, 64);
            ButtonWidget(row, "Kolay", () => SinglePlayerRequested?.Invoke(AiDifficulty.Easy));
            ButtonWidget(row, "Normal", () => SinglePlayerRequested?.Invoke(AiDifficulty.Normal));
            ButtonWidget(row, "Zor", () => SinglePlayerRequested?.Invoke(AiDifficulty.Hard));
            Spacer(col, 18);

            Label(col, "Arkadaşınla oyna", 26, TextColor, FontStyle.Bold, 40);
            ButtonWidget(col, "Oda Kur", () => HostRequested?.Invoke(), 64, AccentColor, Color.black);
            var joinRow = Row(col, 64);
            m_JoinInput = Input(joinRow, "Oda kodu");
            var join = ButtonWidget(joinRow, "Katıl", () => JoinRequested?.Invoke(m_JoinInput.text));
            join.GetComponent<LayoutElement>().flexibleWidth = 0.6f;
            m_MenuStatus = Label(col, string.Empty, 22, BadColor, FontStyle.Normal, 64);
            Label(col, "Fare: raketi yönlendir  •  Yukarı savur: derin vuruş\nSol tık: servis  •  Sol tık basılı: sert vuruş  •  ESC: menü",
                18, MutedColor, FontStyle.Normal, 56);
        }

        void BuildLobby()
        {
            m_Lobby = CenterPanel("Lobby", new Vector2(620, 520));
            var col = m_Lobby.transform;
            Label(col, "Oda kuruldu", 34, TextColor, FontStyle.Bold, 50);
            Label(col, "Oda kodu", 22, MutedColor, FontStyle.Normal, 30);
            m_LobbyCode = Label(col, "-----", 72, AccentColor, FontStyle.Bold, 96);
            m_LobbyHint = Label(col, string.Empty, 20, MutedColor, FontStyle.Normal, 56);
            m_CopyButton = ButtonWidget(col, "Davet Linkini Kopyala", () => CopyInviteRequested?.Invoke());
            m_LobbyStatus = Label(col, string.Empty, 24, TextColor, FontStyle.Italic, 44);
            ButtonWidget(col, "İptal", () => LeaveRequested?.Invoke());
        }

        void BuildConnecting()
        {
            m_Connecting = CenterPanel("Connecting", new Vector2(560, 260));
            var col = m_Connecting.transform;
            m_ConnectingText = Label(col, "Bağlanıyor…", 30, TextColor, FontStyle.Bold, 110);
            ButtonWidget(col, "İptal", () => LeaveRequested?.Invoke());
        }

        void BuildHud()
        {
            m_Hud = new GameObject("HUD", typeof(RectTransform));
            var rt = (RectTransform)m_Hud.transform;
            rt.SetParent(m_Root, false);
            Stretch(rt);

            var board = new GameObject("Scoreboard", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)board.transform;
            brt.SetParent(rt, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0, -16);
            brt.sizeDelta = new Vector2(560, 92);
            board.GetComponent<Image>().color = PanelColor;

            m_NameLeft = Free(brt, "SEN", 20, MutedColor, TextAnchor.MiddleLeft, new Vector2(0, 0.55f), new Vector2(0.4f, 1f), new Vector2(18, 0));
            m_ScoreLeft = Free(brt, "0", 50, TextColor, TextAnchor.MiddleCenter, new Vector2(0.25f, 0f), new Vector2(0.45f, 1f), Vector2.zero, FontStyle.Bold);
            Free(brt, ":", 44, MutedColor, TextAnchor.MiddleCenter, new Vector2(0.45f, 0f), new Vector2(0.55f, 1f), Vector2.zero, FontStyle.Bold);
            m_ScoreRight = Free(brt, "0", 50, TextColor, TextAnchor.MiddleCenter, new Vector2(0.55f, 0f), new Vector2(0.75f, 1f), Vector2.zero, FontStyle.Bold);
            m_NameRight = Free(brt, "RAKİP", 20, MutedColor, TextAnchor.MiddleRight, new Vector2(0.6f, 0.55f), new Vector2(1f, 1f), new Vector2(-18, 0));

            m_Hint = Free(rt, string.Empty, 26, TextColor, TextAnchor.LowerCenter, new Vector2(0.2f, 0f), new Vector2(0.8f, 0.12f), Vector2.zero, FontStyle.Bold);
            AddShadow(m_Hint);
            m_Message = Free(rt, string.Empty, 54, TextColor, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.75f), Vector2.zero, FontStyle.Bold);
            AddShadow(m_Message);
            var esc = Free(rt, "ESC: menü", 18, MutedColor, TextAnchor.UpperLeft, new Vector2(0f, 0.9f), new Vector2(0.3f, 1f), new Vector2(16, -12));
            AddShadow(esc);
        }

        void BuildPause()
        {
            m_Pause = CenterPanel("Pause", new Vector2(480, 300));
            var col = m_Pause.transform;
            Label(col, "Menü", 34, TextColor, FontStyle.Bold, 56);
            ButtonWidget(col, "Oyuna Dön", () => SetPauseOpen(false));
            ButtonWidget(col, "Ana Menüye Dön", () => LeaveRequested?.Invoke());
        }

        void BuildGameOver()
        {
            m_GameOver = CenterPanel("Game Over", new Vector2(560, 420));
            var col = m_GameOver.transform;
            m_GameOverTitle = Label(col, string.Empty, 62, GoodColor, FontStyle.Bold, 86);
            m_GameOverScore = Label(col, string.Empty, 40, TextColor, FontStyle.Bold, 56);
            m_GameOverHint = Label(col, string.Empty, 20, MutedColor, FontStyle.Normal, 34);
            m_RematchButton = ButtonWidget(col, "Tekrar Oyna", () => RematchRequested?.Invoke(), 64, AccentColor, Color.black);
            ButtonWidget(col, "Ana Menüye Dön", () => LeaveRequested?.Invoke());
        }

        // ------------------------------------------------------------------ widgets

        GameObject CenterPanel(string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(m_Root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = PanelColor;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 28, 28);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go;
        }

        Transform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return go.transform;
        }

        void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
        }

        Text Label(Transform parent, string text, int size, Color color, FontStyle style, float height)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            ConfigureText(t, text, size, color, TextAnchor.MiddleCenter, style);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return t;
        }

        Text Free(RectTransform parent, string text, int size, Color color, TextAnchor anchor, Vector2 min, Vector2 max, Vector2 offset, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offset;
            rt.offsetMax = offset;
            var t = go.GetComponent<Text>();
            ConfigureText(t, text, size, color, anchor, style);
            t.raycastTarget = false;
            return t;
        }

        void ConfigureText(Text t, string text, int size, Color color, TextAnchor anchor, FontStyle style)
        {
            t.font = m_Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        Button ButtonWidget(Transform parent, string label, Action onClick, float height = 58, Color? background = null, Color? textColor = null)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            var button = go.GetComponent<Button>();
            var colors = button.colors;
            Color baseColor = background ?? ButtonColor;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.2f);
            colors.selectedColor = baseColor;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var text = Free((RectTransform)go.transform, label, 26, textColor ?? TextColor, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, FontStyle.Bold);
            text.raycastTarget = false;
            return button;
        }

        InputField Input(Transform parent, string placeholder)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.1f, 0.13f, 0.19f, 1f);
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var rt = (RectTransform)go.transform;

            var text = Free(rt, string.Empty, 30, TextColor, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, FontStyle.Bold);
            text.supportRichText = false;
            var hint = Free(rt, placeholder, 26, MutedColor, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, FontStyle.Italic);

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = 24;
            input.onValidateInput += (_, _, c) => char.IsLetterOrDigit(c) || c == '.' || c == ':' ? char.ToUpperInvariant(c) : '\0';
            return input;
        }

        static void AddShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(2f, -2f);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
