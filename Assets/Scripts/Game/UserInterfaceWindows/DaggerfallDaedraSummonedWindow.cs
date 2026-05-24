// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Hazelnut
// Contributors:
//
// Notes:
//

using UnityEngine;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Utility;
using System;
using DaggerfallWorkshop.Game.Questing;

namespace DaggerfallWorkshop.Game.UserInterfaceWindows
{
    /// <summary>
    /// Class for handling Daedra summoning.
    /// </summary>
    public class DaggerfallDaedraSummonedWindow : DaggerfallBaseWindow
    {
        protected const int TextLinesPerChunk = 4;
        protected const int TokensPerChunk = TextLinesPerChunk * 2;

        FLCPlayer playerPanel;
        MultiFormatTextLabel messageLabel;
        TextCursor textCursor;
        TextBox androidTextbox;

        DaggerfallQuestPopupWindow.DaedraData daedraSummoned;
        Quest daedraQuest;

        int textId;
        IMacroContextProvider mcp;

        TextFile.Token[] messageTokens;

        bool lastChunk = false;
        bool answerGiven = false;
        int idx = 0;

        public DaggerfallDaedraSummonedWindow(IUserInterfaceManager uiManager, DaggerfallQuestPopupWindow.DaedraData daedraSummoned, Quest daedraQuest)
            : base(uiManager)
        {
            this.daedraSummoned = daedraSummoned;
            this.daedraQuest = daedraQuest;
        }

        public DaggerfallDaedraSummonedWindow(IUserInterfaceManager uiManager, DaggerfallQuestPopupWindow.DaedraData daedraSummoned, int textId, IMacroContextProvider mcp)
            : base(uiManager)
        {
            this.daedraSummoned = daedraSummoned;
            this.textId = textId;
            this.mcp = mcp;
            answerGiven = true;
        }

        protected override void Setup()
        {
            if (IsSetup)
                return;

            // Initialise the video panel.
            playerPanel = new FLCPlayer()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Middle,
                Size = new Vector2(320, 200),
                BottomMargin = 13
            };
            NativePanel.Components.Add(playerPanel);

            // Start video playing.
            playerPanel.Load(daedraSummoned.vidFile);
            if (playerPanel.FLCFile.ReadyToPlay)
                playerPanel.Start();

            // Add text message area.
            messageLabel = new MultiFormatTextLabel()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                ExtraLeading = 3,
            };
            playerPanel.Components.Add(messageLabel);
            playerPanel.OnMouseClick += PlayerPanel_OnMouseClick;

            textCursor = new TextCursor();
            textCursor.Enabled = false;
            playerPanel.Components.Add(textCursor);

#if UNITY_ANDROID
            androidTextbox = new TextBox();
            androidTextbox.Enabled = false;
            playerPanel.Components.Add(androidTextbox);
#endif
            // Initialise message to display,
            if (daedraQuest != null)
            {   // with the quest offer message.
                Message message = daedraQuest.GetMessage((int)QuestMachine.QuestMessages.QuestorOffer);
                messageTokens = message.GetTextTokens();
            }
            else
            {   // with the textId message evaluated with mcp provided.
                messageTokens = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(textId);
                MacroHelper.ExpandMacros(ref messageTokens, mcp);
            }
            idx = 0;
            DisplayNextTextChunk();     
        }
        public override void Update()
        {
            base.Update();

            if (lastChunk && !answerGiven)
            {
                void startQuest(){
                    HandleAnswer(QuestMachine.QuestMessages.AcceptQuest);
                    QuestMachine.Instance.StartQuest(daedraQuest);
                }
                void refuseQuest(){
                    HandleAnswer(QuestMachine.QuestMessages.RefuseQuest);
                    GameObjectHelper.CreateFoeSpawner(true, DaggerfallQuestPopupWindow.daedricFoes[UnityEngine.Random.Range(0, 5)], UnityEngine.Random.Range(3, 6), 8, 64);
                }
#if UNITY_ANDROID
                if (InputManager.Instance.GetMouseButtonUp(0)){
                    Rect rect = NativePanel.Rectangle;
                    Vector2 screenPos = Vector2.zero;
                    screenPos.x = rect.x + rect.width * (textCursor.Position.x / 320f);
                    screenPos.y = rect.y + rect.height * (1 - textCursor.Position.y / 200f);
                    Vector3 mousePosition = InputManager.Instance.MousePosition;
                    if (mousePosition.y < screenPos.y + 20 && mousePosition.x > screenPos.x - 200)
                        TouchscreenKeyboardManager.Instance.ToggleKeyboardOn(androidTextbox);
                }
                if (androidTextbox.Text.ToLower().Contains('y'))
                    startQuest();
                else if (androidTextbox.Text.ToLower().Contains('n'))
                    refuseQuest();
#endif
                HotkeySequence.KeyModifiers keyModifiers = HotkeySequence.GetKeyboardKeyModifiers();
                if (DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.Yes).IsUpWith(keyModifiers))
                    startQuest();
                else if (DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.No).IsUpWith(keyModifiers))
                    refuseQuest();
            }
        }

        private void HandleAnswer(QuestMachine.QuestMessages qMessage)
        {
            lastChunk = false;
            textCursor.Enabled = false;
            answerGiven = true;
            idx = 0;
            Message message = daedraQuest.GetMessage((int) qMessage);
            messageTokens = message.GetTextTokens();
            playerPanel.OnMouseClick += PlayerPanel_OnMouseClick;
            DisplayNextTextChunk();
        }

        private void DisplayNextTextChunk()
        {
            TextFile.Token[] chunk = new TextFile.Token[TokensPerChunk];
            int len = Math.Min(TokensPerChunk, messageTokens.Length - idx);
            Array.Copy(messageTokens, idx, chunk, 0, len);
            messageLabel.SetText(chunk);
            // Is this the last chunk?
            if (len < TokensPerChunk || idx + len == messageTokens.Length)
            {
                lastChunk = true;
                if (!answerGiven)
                {   // Disable click and listen for Y/N keypress.
                    playerPanel.OnMouseClick -= PlayerPanel_OnMouseClick;
                    textCursor.Position = new Vector2(310, 190);
                    textCursor.Enabled = true;
                    textCursor.BlinkOn();
                }
            }
            idx += TokensPerChunk;
        }

        private void PlayerPanel_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            // If done, close window on click after last chunk, else display next chunk of text.
            if (lastChunk && answerGiven)
                CloseWindow();
            else if (!lastChunk)
                DisplayNextTextChunk();
        }

    }
}
