// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2024 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Vincent Wing (vwing@uci.edu)
// Contributors:
// 
// Notes:
//

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

namespace DaggerfallWorkshop.Game
{
    public class UnityUIPopup : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private TMPro.TMP_Text messageText;
        [SerializeField] private Button buttonYes;
        [SerializeField] private Button buttonNo;

        private System.Action yesAction;
        private System.Action noAction;
        private TMPro.TMP_Text buttonYesText;
        private TMPro.TMP_Text buttonNoText;

        private string buttonYesDefaultString;
        private string buttonNoDefaultString;

        private void Start()
        {
            buttonYesText = buttonYes.GetComponentInChildren<TMPro.TMP_Text>();
            buttonYesDefaultString = buttonYesText.text;
            buttonNoText = buttonNo.GetComponentInChildren<TMPro.TMP_Text>();
            buttonNoDefaultString = buttonNoText.text;
            buttonYes.onClick.AddListener(OnButtonYesPressed);
            buttonNo.onClick.AddListener(OnButtonNoPressed);
        }
        private void OnButtonYesPressed()
        {
            yesAction?.Invoke();
            Close();
        }
        private void OnButtonNoPressed()
        {
            noAction?.Invoke();
            Close();
        }
        public void Open(string text, System.Action yesAction, System.Action noAction = null, string yesButtonString = "", string noButtonString = "")
        {
            canvas.enabled = true;
            messageText.text = text;
            this.yesAction = yesAction;
            this.noAction = noAction;

            buttonYesText.text = string.IsNullOrEmpty(yesButtonString) ? buttonYesDefaultString : yesButtonString;
            buttonNoText.text = string.IsNullOrEmpty(noButtonString) ? buttonNoDefaultString : noButtonString;
        }
        public void Close()
        {
            canvas.enabled = false;
        }
    }
}