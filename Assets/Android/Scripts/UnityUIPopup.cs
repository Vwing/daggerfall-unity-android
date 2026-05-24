// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2024 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Vivian V Wing (vwing@multitude.city)
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
        [SerializeField] private TMPro.TMP_Text buttonYesText;
        [SerializeField] private TMPro.TMP_Text buttonNoText;
        [SerializeField] private TMPro.TMP_InputField inputField;
        [SerializeField] private GameObject inputFieldContainer;
        [SerializeField] private float canvasHeightWithoutInputField = 289.6f;
        [SerializeField] private float canvasHeightWithInputField = 330f;

        private System.Action yesAction;
        private System.Action noAction;
        private System.Action<string> inputAction;

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
            if (inputFieldContainer.activeSelf && inputAction != null)
            {
                inputAction.Invoke(inputField.text);
            }
            yesAction?.Invoke();
            Close();
        }
        private void OnButtonNoPressed()
        {
            noAction?.Invoke();
            Close();
        }
        public void Open(string text, System.Action yesAction, System.Action noAction = null, string yesButtonString = "", string noButtonString = "", bool showNoButton = true, System.Action<string> inputAction = null)
        {
            canvas.enabled = true;
            messageText.text = text;
            this.yesAction = yesAction;
            this.noAction = noAction;
            this.inputAction = inputAction;

            buttonYesText.text = string.IsNullOrEmpty(yesButtonString) ? buttonYesDefaultString : yesButtonString;
            buttonNoText.text = string.IsNullOrEmpty(noButtonString) ? buttonNoDefaultString : noButtonString;
            buttonNo.gameObject.SetActive(showNoButton);
            inputFieldContainer.SetActive(inputAction != null);
            if (inputAction != null)
            {
                inputField.text = "";
                inputField.Select();
            }
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(canvas.GetComponent<RectTransform>().sizeDelta.x, inputAction != null ? canvasHeightWithInputField : canvasHeightWithoutInputField);
        }
        public void Close()
        {
            canvas.enabled = false;
        }
    }
}