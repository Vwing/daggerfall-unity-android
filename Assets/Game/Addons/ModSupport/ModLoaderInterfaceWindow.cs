// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Lypyl (lypyl@dfworkshop.net)
// Contributors:    TheLacus
// 
// Notes:
//

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop;

public class ModLoaderInterfaceWindow : DaggerfallPopupWindow
{
    private enum Stage
    {
        None,
        Cleanup,
        CheckDependencies,
        Close
    }

    struct ModSettings
    {
        public ModInfo modInfo;
        public bool enabled;
    }

    class LooseStreamingAssetFile
    {
        public string SourcePath;
        public string DestinationPath;
    }

    class ImportableModFile
    {
        public string SourcePath;
        public string FileName;
    }

    #region Fields

    DaggerfallMessageBox ModDescriptionMessageBox;

    readonly Panel ModPanel = new Panel();
    readonly Panel ModListPanel = new Panel();
    readonly Panel importProgressPanel = new Panel();
    readonly Panel importProgressBarBg = new Panel();
    readonly Panel importProgressBarFill = new Panel();

    readonly ListBox modList = new ListBox();
    readonly VerticalScrollBar modListScrollBar = new VerticalScrollBar();

    readonly Button increaseLoadOrderButton  = new Button();
    readonly Button decreaseLoadOrderButton  = new Button();
    readonly Button backButton               = new Button();
    readonly Button refreshButton            = new Button();
    readonly Button enableAllButton          = new Button();
    readonly Button disableAllButton         = new Button();
    readonly Button saveAndCloseButton       = new Button();
    readonly Button removeModButton          = new Button();
    readonly Button extractFilesButton       = new Button();
    readonly Button showModDescriptionButton = new Button();
    readonly Button modSettingsButton        = new Button();
    readonly Button importModButton          = new Button();

    readonly Checkbox modEnabledCheckBox         = new Checkbox();
    readonly TextLabel modLoadPriorityLabel      = new TextLabel();
    readonly TextLabel modTitleLabel             = new TextLabel();
    readonly TextLabel modVersionLabel           = new TextLabel();
    readonly TextLabel modAuthorLabel            = new TextLabel();
    readonly TextLabel modAuthorContactLabel     = new TextLabel();
    readonly TextLabel modDFTFUVersionLabel      = new TextLabel();
    readonly TextLabel modsFound                 = new TextLabel();
    readonly TextLabel importProgressText        = new TextLabel();

    readonly Color backgroundColor = new Color(0, 0, 0, 0.7f);
    readonly Color unselectedTextColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    readonly Color selectedTextColor = new Color(0.0f, 0.8f, 0.0f, 1.0f);
    readonly Color textColor = new Color(0.0f, 0.5f, 0.0f, 0.4f);
    readonly Color disabledModTextColor = new Color(0.35f, 0.35f, 0.35f, 1);
    readonly Color disabledButtonBackground = new Color(0.35f, 0.35f, 0.35f, 0.4f);

    Stage currentStage = Stage.None;
    bool moveNextStage = false;
    bool importInProgress = false;

    int currentSelection = -1;
    ModSettings[] modSettings;

    #endregion

    #region Constructors

    public ModLoaderInterfaceWindow(IUserInterfaceManager uiManager)
    : base(uiManager)
    {
    }

    #endregion

    #region Methods

    protected override void Setup()
    {
        ParentPanel.BackgroundColor = Color.clear;

        ModListPanel.Outline.Enabled = true;
        ModListPanel.BackgroundColor = backgroundColor;
        ModListPanel.HorizontalAlignment = HorizontalAlignment.Left;
        ModListPanel.VerticalAlignment = VerticalAlignment.Middle;
        ModListPanel.Size = new Vector2(120, 175);
        NativePanel.Components.Add(ModListPanel);

        modsFound.HorizontalAlignment = HorizontalAlignment.Center;
        modsFound.Position = new Vector2(10, 20);
        modsFound.Text = string.Format("{0}: ", ModManager.GetText("modsFound"));
        ModListPanel.Components.Add(modsFound);

        importModButton.Position = new Vector2(10, 2);
        importModButton.Size = new Vector2(40, 12);
        importModButton.HorizontalAlignment = HorizontalAlignment.Center;
        importModButton.Label.Text = ModManager.GetText("importMod");
        importModButton.BackgroundColor = textColor;
        importModButton.Outline.Enabled = true;
        importModButton.OnMouseClick += ImportMod_OnMouseClick;
        ModListPanel.Components.Add(importModButton);

        modList.BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        modList.Size = new Vector2(110, 115);
        modList.HorizontalAlignment = HorizontalAlignment.Center;
        modList.VerticalAlignment = VerticalAlignment.Middle;
        modList.TextColor = unselectedTextColor;
        modList.SelectedTextColor = textColor;
        modList.ShadowPosition = Vector2.zero;
        modList.RowsDisplayed = 14;
        modList.RowAlignment = HorizontalAlignment.Left;
        modList.LeftMargin += 4;
        modList.SelectedShadowPosition = DaggerfallUI.DaggerfallDefaultShadowPos;
        modList.SelectedShadowColor = Color.black;
        modList.OnScroll += ModList_OnScroll;
        ModListPanel.Components.Add(modList);

        modListScrollBar.Size = new Vector2(5, 115);
        modListScrollBar.HorizontalAlignment = HorizontalAlignment.Right;
        modListScrollBar.VerticalAlignment = VerticalAlignment.Middle;
        modListScrollBar.Position = new Vector2(100, 12);
        modListScrollBar.BackgroundColor = Color.grey;
        modListScrollBar.DisplayUnits = 14;
        modListScrollBar.TotalUnits = modList.Count;
        modListScrollBar.OnScroll += ModListScrollBar_OnScroll;
        ModListPanel.Components.Add(modListScrollBar);
        modList.ScrollToSelected();

        backButton.Size = new Vector2(45, 12);
        backButton.Label.Text = string.Format("< {0}", ModManager.GetText("backToOptions"));
        backButton.Label.ShadowPosition = Vector2.zero;
        backButton.Label.TextColor = Color.gray;
        backButton.ToolTip = defaultToolTip;
        backButton.ToolTipText = ModManager.GetText("backToOptionsInfo");
        backButton.VerticalAlignment = VerticalAlignment.Top;
        backButton.HorizontalAlignment = HorizontalAlignment.Left;
        backButton.OnMouseClick +=  BackButton_OnMouseClick;
        backButton.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.GameSetupBackToOptions);
        ModListPanel.Components.Add(backButton);

        increaseLoadOrderButton.Size = new Vector2(40, 12);
        increaseLoadOrderButton.Position = new Vector2(62, 150);
        increaseLoadOrderButton.Outline.Enabled = true;
        increaseLoadOrderButton.BackgroundColor = textColor;
        increaseLoadOrderButton.Label.Text = ModManager.GetText("increase");
        increaseLoadOrderButton.OnMouseClick += IncreaseLoadOrderButton_OnMouseClick;
        ModListPanel.Components.Add(increaseLoadOrderButton);

        decreaseLoadOrderButton.Size = new Vector2(40, 12);
        decreaseLoadOrderButton.Position = new Vector2(21, 150);
        decreaseLoadOrderButton.Outline.Enabled = true;
        decreaseLoadOrderButton.BackgroundColor = textColor;
        decreaseLoadOrderButton.Label.Text = ModManager.GetText("lower");
        decreaseLoadOrderButton.OnMouseClick += DecreaseLoadOrderButton_OnMouseClick;
        ModListPanel.Components.Add(decreaseLoadOrderButton);

        enableAllButton.Size = new Vector2(40, 12);
        enableAllButton.Position = new Vector2(21, 163);
        enableAllButton.Outline.Enabled = true;
        enableAllButton.BackgroundColor = textColor;
        enableAllButton.VerticalAlignment = VerticalAlignment.Bottom;
        enableAllButton.Label.Text = ModManager.GetText("enableAll");
        enableAllButton.ToolTipText = ModManager.GetText("enableAllInfo");
        enableAllButton.OnMouseClick += EnableAllButton_OnMouseClick;
        ModListPanel.Components.Add(enableAllButton);

        disableAllButton.Size = new Vector2(40, 12);
        disableAllButton.Position = new Vector2(62, 163);
        disableAllButton.Outline.Enabled = true;
        disableAllButton.BackgroundColor = textColor;
        disableAllButton.VerticalAlignment = VerticalAlignment.Bottom;
        disableAllButton.Label.Text = ModManager.GetText("disableAll");
        disableAllButton.ToolTipText = ModManager.GetText("disableAllInfo");
        disableAllButton.OnMouseClick += DisableAllButton_OnMouseClick;
        ModListPanel.Components.Add(disableAllButton);

        //Add main mod panel
        ModPanel.Outline.Enabled = true;
        ModPanel.BackgroundColor = backgroundColor;
        ModPanel.HorizontalAlignment = HorizontalAlignment.Right;
        ModPanel.VerticalAlignment = VerticalAlignment.Middle;
        ModPanel.Size = new Vector2(200, 175);
        NativePanel.Components.Add(ModPanel);

        modEnabledCheckBox.Label.Text = ModManager.GetText("enabled");
        modEnabledCheckBox.Label.TextColor = selectedTextColor;
        modEnabledCheckBox.CheckBoxColor = selectedTextColor;
        modEnabledCheckBox.ToolTip = defaultToolTip;
        modEnabledCheckBox.ToolTipText = ModManager.GetText("enabledInfo");
        modEnabledCheckBox.IsChecked = true;
        modEnabledCheckBox.Position = new Vector2(1, 25);
        modEnabledCheckBox.OnToggleState += ModEnabledCheckBox_OnToggleState;
        ModPanel.Components.Add(modEnabledCheckBox);

        modLoadPriorityLabel.Position = new Vector2(60, 25);
        ModPanel.Components.Add(modLoadPriorityLabel);

        modTitleLabel.Position = new Vector2(0, 5);
        modTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        modTitleLabel.MaxCharacters = 40;
        ModPanel.Components.Add(modTitleLabel);

        modVersionLabel.Position = new Vector2(5, 40);
        modVersionLabel.MaxCharacters = 40;
        ModPanel.Components.Add(modVersionLabel);

        modAuthorLabel.Position = new Vector2(5, 50);
        modAuthorLabel.MaxCharacters = 40;
        ModPanel.Components.Add(modAuthorLabel);

        modAuthorContactLabel.Position = new Vector2(5, 60);
        modAuthorContactLabel.MaxCharacters = 40;
        ModPanel.Components.Add(modAuthorContactLabel);

        modDFTFUVersionLabel.Position = new Vector2(5, 70);
        modDFTFUVersionLabel.MaxCharacters = 40;
        ModPanel.Components.Add(modDFTFUVersionLabel);

        showModDescriptionButton.Position = new Vector2(5, 95);
        showModDescriptionButton.Size = new Vector2(75, 12);
        showModDescriptionButton.HorizontalAlignment = HorizontalAlignment.Center;
        showModDescriptionButton.Label.Text = ModManager.GetText("modDescription");
        showModDescriptionButton.BackgroundColor = textColor;
        showModDescriptionButton.Outline.Enabled = true;
        showModDescriptionButton.OnMouseClick += ShowModDescriptionPopUp_OnMouseClick;
        ModPanel.Components.Add(showModDescriptionButton);

        refreshButton.Size = new Vector2(50, 12);
        refreshButton.Position = new Vector2(5, 139);
        refreshButton.Outline.Enabled = true;
        refreshButton.BackgroundColor = textColor;
        refreshButton.HorizontalAlignment = HorizontalAlignment.Center;
        refreshButton.Label.Text = ModManager.GetText("refresh");
        refreshButton.Label.ToolTipText = ModManager.GetText("RrefreshInfo");
        refreshButton.OnMouseClick += RefreshButton_OnMouseClick;
        refreshButton.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.GameSetupRefresh);
        ModPanel.Components.Add(refreshButton);

        saveAndCloseButton.Size = new Vector2(70, 12);
        saveAndCloseButton.Outline.Enabled = true;
        saveAndCloseButton.BackgroundColor = textColor;
        saveAndCloseButton.VerticalAlignment = VerticalAlignment.Bottom;
        saveAndCloseButton.HorizontalAlignment = HorizontalAlignment.Center;
        saveAndCloseButton.Label.Text = ModManager.GetText("saveClose");
        saveAndCloseButton.Label.ToolTipText = ModManager.GetText("saveCloseInfo");
        saveAndCloseButton.OnMouseClick += SaveAndCloseButton_OnMouseClick;
        saveAndCloseButton.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.GameSetupSaveAndClose);
        ModPanel.Components.Add(saveAndCloseButton);
        
        extractFilesButton.Size = new Vector2(60, 12);
        extractFilesButton.Position = new Vector2(5, 117);
        extractFilesButton.Outline.Enabled = true;
        extractFilesButton.BackgroundColor = textColor;
        extractFilesButton.HorizontalAlignment = HorizontalAlignment.Center;
        extractFilesButton.Label.Text = ModManager.GetText("extractText");
        extractFilesButton.Label.ToolTipText = ModManager.GetText("extractTextInfo");
        extractFilesButton.OnMouseClick += ExtractFilesButton_OnMouseClick;
        ModPanel.Components.Add(extractFilesButton);

        modSettingsButton.Size = new Vector2(60, 12);
        modSettingsButton.Position = new Vector2(5, 103);
        modSettingsButton.Outline.Enabled = true;
        modSettingsButton.BackgroundColor = textColor;
        modSettingsButton.HorizontalAlignment = HorizontalAlignment.Center;
        modSettingsButton.Label.Text = ModManager.GetText("settings");
        modSettingsButton.Label.ToolTipText = ModManager.GetText("settingsInfo");
        modSettingsButton.OnMouseClick += ModSettingsButton_OnMouseClick;
        modSettingsButton.Enabled = false;
        ModPanel.Components.Add(modSettingsButton);

#if UNITY_ANDROID
        removeModButton.Size = new Vector2(50, 12);
        removeModButton.Outline.Enabled = true;
        removeModButton.BackgroundColor = textColor;
        removeModButton.VerticalAlignment = VerticalAlignment.Bottom;
        removeModButton.HorizontalAlignment = HorizontalAlignment.Right;
        removeModButton.Label.Text = ModManager.GetText("removeMod");
        removeModButton.Label.ToolTipText = ModManager.GetText("removeModInfo");
        removeModButton.OnMouseClick += RemoveModButton_OnMouseClick;
        ModPanel.Components.Add(removeModButton);
#endif

        GetLoadedMods();
        UpdateModPanel();
        SetupImportProgressPanel();
    }

    public override void Update()
    {
        base.Update();

        if(currentSelection != modList.SelectedIndex && modList.Count > 0)
        {
            currentSelection = modList.SelectedIndex;
            UpdateModPanel();
        }

        modListScrollBar.TotalUnits = modList.Count;
        modListScrollBar.DisplayUnits = modList.RowsDisplayed;

        if (modListScrollBar.DraggingThumb)
        {
            modList.ScrollIndex = modListScrollBar.ScrollIndex;
        }
        else
        {
            modListScrollBar.ScrollIndex = modList.ScrollIndex;
        }

        if (moveNextStage)
        {
            moveNextStage = false;
            MoveNextStage();
        }
    }

    bool GetModSettings(ref ModSettings ms)
    {
         if (modList.SelectedIndex < 0 || modList.SelectedIndex > modSettings.Count())
            return false;

         ms = modSettings[modList.SelectedIndex];
         return ms.modInfo != null;
    }

    void GetLoadedMods()
    {
        var mods = ModManager.Instance.GetAllMods();

        modList.ClearItems();

        if(modSettings == null || modSettings.Length != mods.Length)
        {
            modSettings = new ModSettings[mods.Length];
        }

        for (int i = 0; i < mods.Length; i++)
        {
            ModSettings modsett = new ModSettings();
            modsett.modInfo = mods[i].ModInfo;
            modsett.enabled = mods[i].Enabled;
            modSettings[i] = modsett;
            modList.AddItem(modsett.modInfo.ModTitle, out ListBox.ListItem item);
            item.textColor = modsett.enabled ? unselectedTextColor : disabledModTextColor;
        }

        if (modList.SelectedIndex < 0 || modList.SelectedIndex >= modList.Count)
        {
            modList.SelectedIndex = 0;
        }
        mods = null;
    }

    void UpdateModPanel()
    {
        modLoadPriorityLabel.Text   = string.Format("{0}: ", ModManager.GetText("modLoadPriority"));
        modTitleLabel.Text          = string.Format("{0}: ", ModManager.GetText("modTitle"));
        modVersionLabel.Text        = string.Format("{0}: ", ModManager.GetText("modVersion"));
        modAuthorLabel.Text         = string.Format("{0}: ", ModManager.GetText("modAuthor"));
        modAuthorContactLabel.Text  = string.Format("{0}: ", ModManager.GetText("modAuthorContact"));
        modDFTFUVersionLabel.Text   = string.Format("{0}: ", ModManager.GetText("modDFTFUVersion"));

        if (modSettings.Length < 1 || currentSelection < 0)
        {
            return;
        }

        ModSettings ms = modSettings[modList.SelectedIndex];

        if (ms.modInfo == null)
            return;

        modEnabledCheckBox.IsChecked = ms.enabled;
        modLoadPriorityLabel.Text   += modList.SelectedIndex;
        modTitleLabel.Text          += ms.modInfo.ModTitle;
        modVersionLabel.Text        += ms.modInfo.ModVersion;
        modAuthorLabel.Text         += ms.modInfo.ModAuthor;
        modAuthorContactLabel.Text  += ms.modInfo.ContactInfo;
        modDFTFUVersionLabel.Text   += ms.modInfo.DFUnity_Version;

        Mod mod = ModManager.Instance.GetMod(ms.modInfo.ModTitle);

        modDFTFUVersionLabel.TextColor = mod.IsGameVersionSatisfied() == false ? Color.red : DaggerfallUI.DaggerfallDefaultTextColor;

#if UNITY_EDITOR
        if (mod.IsVirtual)
            modTitleLabel.Text += " (debug)";
#endif

        bool hasDescription = !string.IsNullOrWhiteSpace(ms.modInfo.ModDescription);
        showModDescriptionButton.BackgroundColor = hasDescription ? textColor : disabledButtonBackground;

        // Update buttons
        if (mod.HasSettings)
        {
            modSettingsButton.Enabled = true;
            showModDescriptionButton.Position = new Vector2(5, 83);
            extractFilesButton.Position = new Vector2(5, 123);
            refreshButton.Position = new Vector2(5, 143);
        }
        else
        {
            modSettingsButton.Enabled = false;
            showModDescriptionButton.Position = new Vector2(5, 95);
            extractFilesButton.Position = new Vector2(5, 117);
            refreshButton.Position = new Vector2(5, 139);
        }
    }

    private void CleanConfigurationDirectory()
    {
        var unknownDirectories = Directory.GetDirectories(ModManager.Instance.ModDataDirectory)
            .Select(x => new DirectoryInfo(x))
            .Where(x => ModManager.Instance.GetModFromGUID(x.Name) == null)
            .ToArray();

        if (unknownDirectories.Length > 0)
        {
            void yesAction(){
                foreach (var directory in unknownDirectories)
                    directory.Delete(true);
                moveNextStage = true;
            }
            void noAction(){
                moveNextStage = true;
            }
            ShowConfirmationBox(ModManager.GetText("cleanConfigurationDir"), yesAction, noAction);
        }
        else
        {
            moveNextStage = true;
        }
    }

    private void CheckDependencies()
    {
        bool hasSortIssues = false;
        List<string> errorMessages = null;
        var modErrorMessages = new List<string>();
        
        foreach (Mod mod in ModManager.Instance.Mods.Where(x => x.Enabled))
        {
            bool? isGameVersionSatisfied = mod.IsGameVersionSatisfied();
            if (!isGameVersionSatisfied.HasValue)
                Debug.LogErrorFormat("Mod {0} requires unknown game version ({1}).", mod.Title, mod.ModInfo.DFUnity_Version);
            else if (!isGameVersionSatisfied.Value)
                modErrorMessages.Add(string.Format(ModManager.GetText("gameVersionUnsatisfied"), mod.ModInfo.DFUnity_Version));

            ModManager.Instance.CheckModDependencies(mod, modErrorMessages, ref hasSortIssues);
            if (modErrorMessages.Count > 0)
            {
                if (errorMessages == null)
                {
                    errorMessages = new List<string>();
                    errorMessages.Add(ModManager.GetText("dependencyErrorMessage"));
                    errorMessages.Add(string.Empty);
                }

                errorMessages.Add(string.Format("- {0}", mod.Title));
                errorMessages.AddRange(modErrorMessages);
                errorMessages.Add(string.Empty);
                modErrorMessages.Clear();
            }
        }

        if (errorMessages != null && errorMessages.Count > 0)
        {
            if (hasSortIssues)
                errorMessages.Add(ModManager.GetText("sortModsQuestion"));

            var messageBox = new DaggerfallMessageBox(uiManager, this);
            messageBox.EnableVerticalScrolling(80);
            messageBox.SetText(errorMessages.ToArray());
            if (hasSortIssues)
            {
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
                messageBox.OnButtonClick += (sender, button) =>
                {
                    if (button == DaggerfallMessageBox.MessageBoxButtons.Yes)
                    {
                        ModManager.Instance.AutoSortMods();
                        Debug.Log("Mods have been sorted automatically");
                    }

                    sender.CancelWindow();
                    moveNextStage = true;
                };
            }
            else
            {
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.OK, true);
                messageBox.OnButtonClick += (sender, button) =>
                {
                    sender.CancelWindow();
                    moveNextStage = true;
                };
            }
            messageBox.Show();
        }
        else
        {
            moveNextStage = true;
        }
    }

    private void SaveAndClose()
    {
        ModManager.WriteModSettings();
        CloseWindow();
    }

    private void MoveNextStage()
    {
        switch (currentStage = (Stage)((int)currentStage + 1))
        {
            case Stage.Cleanup:
                CleanConfigurationDirectory();
                break;
            case Stage.CheckDependencies:
                CheckDependencies();
                break;
            default:
                SaveAndClose();
                break;
        }
    }

    #endregion

    #region Events

    void DecreaseLoadOrderButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modList.Count < 2)
            return;
        else if (modList.SelectedIndex == modList.Count - 1)    //last index already
            return;

        modList.SwapItems(modList.SelectedIndex, modList.SelectedIndex + 1);

        ModSettings temp = modSettings[modList.SelectedIndex];
        modSettings[modList.SelectedIndex] = modSettings[modList.SelectedIndex + 1];
        modSettings[modList.SelectedIndex + 1] = temp;

        modList.SelectedIndex++;
    }

    void IncreaseLoadOrderButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modList.Count < 2)
            return;
        else if (modList.SelectedIndex == 0)    //first priority already
            return;

        modList.SwapItems(modList.SelectedIndex, modList.SelectedIndex - 1);

        ModSettings temp = modSettings[modList.SelectedIndex];
        modSettings[modList.SelectedIndex] = modSettings[modList.SelectedIndex - 1];
        modSettings[modList.SelectedIndex - 1] = temp;

        modList.SelectedIndex--;
    }

    void RefreshButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        ModManager.Instance.Refresh();
        int count = modSettings.Length;
        GetLoadedMods();
        if (modSettings.Length != count)
            currentSelection = -1;
        UpdateModPanel();
    }

    void SaveAndCloseButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modSettings == null)
        {
            return;
        }

        for (int i = 0; i < modSettings.Length; i++)
        {
            Mod mod = ModManager.Instance.GetMod(modSettings[i].modInfo.ModTitle);
            if (mod == null)
                continue;
            mod.Enabled = modSettings[i].enabled;
            mod.LoadPriority = i;
            mod = null;
        }

        ModManager.Instance.SortMods();
        MoveNextStage();
    }

    void ExtractFilesButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modSettings.Length < 1)
            return;

        Mod mod = ModManager.Instance.GetMod(modSettings[modList.SelectedIndex].modInfo.ModTitle);

        if (mod == null)
        {
            return;
        }

        string[] assets = mod.AssetNames;
        if (assets == null)
            return;

        string path = Path.Combine(DaggerfallWorkshop.Paths.PersistentDataPath, "Mods", "ExtractedFiles", mod.FileName);
        Directory.CreateDirectory(path);

        for (int i = 0; i < assets.Length; i++)
        {
            string extension = Path.GetExtension(assets[i]);
            if (!ModManager.textExtensions.Contains(extension))
                continue;

            var asset = mod.GetAsset<TextAsset>(assets[i]);
            if (asset == null)
                continue;

            if (assets[i].EndsWith(".bytes", StringComparison.Ordinal))
            {
                // Export binary asset without .bytes extension
                File.WriteAllBytes(Path.Combine(path, asset.name), asset.bytes);
            }
            else if (assets[i].EndsWith(".cs.txt", StringComparison.Ordinal))
            {
                // Export C# script without .txt extension
                File.WriteAllText(Path.Combine(path, asset.name), asset.text);
            }
            else
            {
                // Export text asset with original extension
                File.WriteAllText(Path.Combine(path, asset.name + extension), asset.text);
            }
        }

        var messageBox = new DaggerfallMessageBox(uiManager, this, true);
        messageBox.AllowCancel = true;
        messageBox.ClickAnywhereToClose = true;
        messageBox.ParentPanel.BackgroundTexture = null;
        messageBox.SetText(string.Format(ModManager.GetText("extractTextConfirmation"), path));
        uiManager.PushWindow(messageBox);
    }

    void BackButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        DaggerfallUI.UIManager.PopWindow();
    }

    void EnableAllButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modSettings == null || modSettings.Length < 1)
            return;

        for (int i = 0; i < modSettings.Length; i++)
        {
            modSettings[i].enabled = true;
            modList.GetItem(i).textColor = unselectedTextColor;
        }
        UpdateModPanel();
    }

    void DisableAllButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modSettings == null || modSettings.Length < 1)
            return;

        for (int i = 0; i < modSettings.Length; i++)
        {
            modSettings[i].enabled = false;
            modList.GetItem(i).textColor = disabledModTextColor;
        }

        UpdateModPanel();
    }

    void OnImportedModFilePicked(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || importInProgress)
            return;

        importInProgress = true;
        ShowImportProgress();
        SetImportProgress(0f, "Importing mod...");
        CoroutineManager.Instance.StartCoroutine(ImportModFileCoroutine(filePath));
    }

    private IEnumerator ImportModFileCoroutine(string filePath)
    {
        IEnumerator operation = ImportModFile(filePath);
        while (true)
        {
            object current = null;
            bool moveNext = false;

            try
            {
                moveNext = operation.MoveNext();
                if (moveNext)
                    current = operation.Current;
            }
            catch (UnauthorizedAccessException ex)
            {
                HandleModImportUnauthorizedAccess(ex);
                yield break;
            }
            catch (Exception ex)
            {
                HandleModImportError(ex);
                yield break;
            }

            if (!moveNext)
            {
                CleanupImportProgress();
                yield break;
            }

            yield return current;
        }
    }

    private void HandleModImportUnauthorizedAccess(UnauthorizedAccessException ex)
    {
        Debug.LogWarning("Android denied access while importing mod: " + ex.Message);
        HideImportProgress();
        importInProgress = false;
        DeleteImportCache();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!AndroidUtils.HasAllFilesAccess())
        {
            ShowAllFilesAccessBox("Android denied access while importing this mod. Grant Daggerfall Unity all files access, then import the mod again.");
            return;
        }
#endif

        ShowMessageBox("Could not import this mod because Android denied file access.");
    }

    private void HandleModImportError(Exception ex)
    {
        Debug.LogWarning("Failed to import mod: " + ex.Message);
        HideImportProgress();
        importInProgress = false;
        DeleteImportCache();
        ShowMessageBox("Could not import this mod: " + ex.Message);
    }

    private void CleanupImportProgress()
    {
        if (importInProgress)
        {
            HideImportProgress();
            importInProgress = false;
        }
    }

    private void DeleteImportCache()
    {
        string cachePath = Path.Combine(Application.temporaryCachePath, "ImportedMods");
        try
        {
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to clean imported mod cache: " + ex.Message);
        }
    }

    IEnumerator ImportModFile(string filePath)
    {
        string modsFolderPath = Path.Combine(DaggerfallWorkshop.Paths.StreamingAssetsPath, "Mods");
        if (!Directory.Exists(modsFolderPath))
            Directory.CreateDirectory(modsFolderPath);

        bool upgradedMod = false;

        if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Extract zip and copy any .dfmod files contained to mods folder
            string cachePath = Path.Combine(Application.temporaryCachePath, "ImportedMods");
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, true);
            Directory.CreateDirectory(cachePath);

            SetImportProgress(0f, "Unzipping...");
            yield return null;
            yield return CoroutineManager.Instance.StartCoroutine(DaggerfallWorkshop.Utility.ZipFileUtils.UnzipFileAsync(filePath, cachePath,
                (progress) =>
                {
                    SetImportProgress(progress * 35f, "Unzipping...");
                }));

            // Find any .dfmod files within the unzipped path that can load on this platform.
            SetImportProgress(35f, "Checking mods...");
            yield return null;
            string[] modFiles = Directory.GetFiles(cachePath, "*.dfmod", SearchOption.AllDirectories);
            List<ImportableModFile> importableMods = GetImportableModFiles(modFiles, 35f, 55f);
            foreach (ImportableModFile modFile in importableMods)
            {
                string destFile = Path.Combine(modsFolderPath, modFile.FileName);
                if (File.Exists(destFile)){
                    Debug.LogWarning($"DFMod file already exists: {destFile}. Overwriting it!");
                    upgradedMod = true;
                }
                File.Copy(modFile.SourcePath, destFile, true);
                File.Delete(modFile.SourcePath);
                SetImportProgress(70f, "Copying mods...");
                yield return null;
            }

            if (modFiles.Length > 0 && importableMods.Count == 0)
            {
                Directory.Delete(cachePath, true);
                CleanupImportProgress();
                ShowMessageBox("This mod zip does not contain a Daggerfall Unity mod built for Android. Download the Android version of the mod and import that file instead.");
                yield break;
            }

            SetImportProgress(75f, "Checking assets...");
            yield return null;
            List<LooseStreamingAssetFile> looseStreamingAssets = GetLooseStreamingAssetFiles(cachePath);
            if (looseStreamingAssets.Count > 0)
            {
                HideImportProgress();
                importInProgress = false;
                PromptForLooseStreamingAssetsImport(looseStreamingAssets, cachePath, upgradedMod);
                yield break;
            }

            Directory.Delete(cachePath, true);
        }
        else if (filePath.EndsWith(".dfmod", StringComparison.OrdinalIgnoreCase))
        {
            SetImportProgress(20f, "Checking mod...");
            yield return null;
            if (!IsLoadableModFile(filePath))
            {
                CleanupImportProgress();
                ShowMessageBox("This Daggerfall Unity mod file is not compatible with Android. Download the Android version of the mod and import that file instead.");
                yield break;
            }

            // Copy .dfmod to mods folder
            SetImportProgress(65f, "Copying mod...");
            yield return null;
            string destFilePath = Path.Combine(modsFolderPath, Path.GetFileName(filePath));
            if (File.Exists(destFilePath)){
                Debug.LogWarning($"File already exists: {destFilePath}. Overwriting it!");
                upgradedMod = true;
            }
            File.Copy(filePath, destFilePath, true);
        } else {
            // inform user that the only valid filters are .zip and .dfmod
            CleanupImportProgress();
            ShowMessageBox("Only .zip and .dfmod files are supported for import.");
            yield break;
        }
        SetImportProgress(90f, "Refreshing mods...");
        yield return null;
        RefreshButton_OnMouseClick(null, Vector2.zero);
        SetImportProgress(100f, "Done...");
        yield return new WaitForSecondsRealtime(0.2f);
        if (upgradedMod){
            ShowConfirmationBox("A mod was upgraded; this requires restarting the game. Restart now?", AndroidUtils.RestartAndroid, null);
        }
    }

    private List<ImportableModFile> GetImportableModFiles(string[] modFiles, float startPercent, float endPercent)
    {
        List<ImportableModFile> importableMods = new List<ImportableModFile>();
        if (modFiles.Length == 0)
            return importableMods;

        for (int i = 0; i < modFiles.Length; i++)
        {
            string file = modFiles[i];
            if (!IsLoadableModFile(file))
            {
                Debug.LogWarning($"Skipping incompatible or invalid mod file: {file}");
                continue;
            }

            importableMods.Add(new ImportableModFile()
            {
                SourcePath = file,
                FileName = Path.GetFileName(file),
            });

            float percent = startPercent + (endPercent - startPercent) * (i + 1) / modFiles.Length;
            SetImportProgress(percent, "Checking mods...");
        }

        return importableMods;
    }

    private static bool IsLoadableModFile(string filePath)
    {
        AssetBundle assetBundle = null;
        try
        {
            assetBundle = AssetBundle.LoadFromFile(filePath);
            if (assetBundle == null)
                return false;

            return assetBundle.GetAllAssetNames()
                .Any(name => name.EndsWith(ModManager.MODINFOEXTENSION, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to validate mod file {filePath}: {ex.Message}");
            return false;
        }
        finally
        {
            if (assetBundle != null)
                assetBundle.Unload(false);
        }
    }

    private List<LooseStreamingAssetFile> GetLooseStreamingAssetFiles(string extractedPath)
    {
        List<LooseStreamingAssetFile> files = new List<LooseStreamingAssetFile>();
        string streamingAssetsPath = DaggerfallWorkshop.Paths.StreamingAssetsPath;
        if (!Directory.Exists(extractedPath) || !Directory.Exists(streamingAssetsPath))
            return files;

        HashSet<string> streamingAssetFolderNames = new HashSet<string>();
        foreach (string directory in Directory.GetDirectories(streamingAssetsPath))
        {
            string folderName = Path.GetFileName(directory);
            if (folderName != "Mods")
                streamingAssetFolderNames.Add(folderName);
        }

        List<string> matchingDirectories = Directory.GetDirectories(extractedPath, "*", SearchOption.AllDirectories)
            .Where(directory => streamingAssetFolderNames.Contains(Path.GetFileName(directory)))
            .OrderBy(directory => directory.Length)
            .ToList();

        List<string> roots = new List<string>();
        foreach (string directory in matchingDirectories)
        {
            if (!roots.Any(root => IsPathInside(root, directory)))
                roots.Add(directory);
        }

        HashSet<string> destinationPaths = new HashSet<string>();
        foreach (string root in roots)
        {
            string streamingAssetsFolderName = Path.GetFileName(root);
            foreach (string sourceFile in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.Combine(streamingAssetsFolderName, GetRelativePath(root, sourceFile));
                string destinationPath = Path.Combine(streamingAssetsPath, relativePath);
                if (destinationPaths.Add(destinationPath))
                {
                    files.Add(new LooseStreamingAssetFile()
                    {
                        SourcePath = sourceFile,
                        DestinationPath = destinationPath,
                    });
                }
            }
        }

        return files;
    }

    private void PromptForLooseStreamingAssetsImport(List<LooseStreamingAssetFile> looseStreamingAssets, string cachePath, bool upgradedMod)
    {
        int overwriteCount = looseStreamingAssets.Count(file => File.Exists(file.DestinationPath));
        string overwriteText = overwriteCount > 0
            ? string.Format(" {0} existing file{1} will be overwritten.", overwriteCount, overwriteCount == 1 ? string.Empty : "s")
            : " No existing files will be overwritten.";

        DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, this, true);
        messageBox.SetText(string.Format(
            "This mod zip contains {0} loose StreamingAssets file{1}.{2} Import these files?",
            looseStreamingAssets.Count,
            looseStreamingAssets.Count == 1 ? string.Empty : "s",
            overwriteText));
        messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
        messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
        messageBox.OnButtonClick += (sender, messageBoxButton) =>
        {
            sender.CloseWindow();

            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes)
            {
                importInProgress = true;
                ShowImportProgress();
                SetImportProgress(75f, "Copying assets...");
                CoroutineManager.Instance.StartCoroutine(ImportLooseStreamingAssetsWithHandling(looseStreamingAssets, cachePath, upgradedMod));
                return;
            }

            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, true);

            FinishModImport(upgradedMod);
        };
        uiManager.PushWindow(messageBox);
    }

    private IEnumerator ImportLooseStreamingAssetsWithHandling(List<LooseStreamingAssetFile> looseStreamingAssets, string cachePath, bool upgradedMod)
    {
        IEnumerator operation = ImportLooseStreamingAssetsCoroutine(looseStreamingAssets, cachePath, upgradedMod);
        while (true)
        {
            object current = null;
            bool moveNext = false;

            try
            {
                moveNext = operation.MoveNext();
                if (moveNext)
                    current = operation.Current;
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogWarning("Android denied access while importing loose StreamingAssets files: " + ex.Message);
                CleanupImportProgress();

                if (Directory.Exists(cachePath))
                    Directory.Delete(cachePath, true);

#if UNITY_ANDROID && !UNITY_EDITOR
                if (!AndroidUtils.HasAllFilesAccess())
                {
                    ShowAllFilesAccessBox("Android denied access while importing loose StreamingAssets files. Grant Daggerfall Unity all files access, then import the mod again.");
                    yield break;
                }
#endif

                ShowMessageBox("Could not import loose StreamingAssets files because Android denied file access.");
                yield break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to import loose StreamingAssets files: " + ex.Message);
                CleanupImportProgress();

                if (Directory.Exists(cachePath))
                    Directory.Delete(cachePath, true);

                ShowMessageBox("Could not import loose StreamingAssets files: " + ex.Message);
                yield break;
            }

            if (!moveNext)
                yield break;

            yield return current;
        }
    }

    private IEnumerator ImportLooseStreamingAssetsCoroutine(List<LooseStreamingAssetFile> looseStreamingAssets, string cachePath, bool upgradedMod)
    {
        for (int i = 0; i < looseStreamingAssets.Count; i++)
        {
            LooseStreamingAssetFile file = looseStreamingAssets[i];
            Directory.CreateDirectory(Path.GetDirectoryName(file.DestinationPath));
            File.Copy(file.SourcePath, file.DestinationPath, true);

            float percent = 75f + 20f * (i + 1) / looseStreamingAssets.Count;
            SetImportProgress(percent, "Copying assets...");
            if (i % 10 == 0)
                yield return null;
        }

        if (Directory.Exists(cachePath))
            Directory.Delete(cachePath, true);

        SetImportProgress(95f, "Refreshing mods...");
        yield return null;
        FinishModImport(upgradedMod);
        SetImportProgress(100f, "Done...");
        yield return new WaitForSecondsRealtime(0.2f);
        CleanupImportProgress();
    }

    private void FinishModImport(bool upgradedMod)
    {
        RefreshButton_OnMouseClick(null, Vector2.zero);
        if (upgradedMod)
            ShowConfirmationBox("A mod was upgraded; this requires restarting the game. Restart now?", AndroidUtils.RestartAndroid, null);
    }

    private static bool IsPathInside(string parentPath, string childPath)
    {
        string parent = parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string child = childPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.Ordinal);
    }

    private static string GetRelativePath(string rootPath, string path)
    {
        string root = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.Substring(root.Length);
    }

    private void ShowMessageBox(string text)
    {
        var messageBox = new DaggerfallMessageBox(uiManager, this, true);
        messageBox.AllowCancel = true;
        messageBox.ClickAnywhereToClose = true;
        messageBox.ParentPanel.BackgroundTexture = null;
        messageBox.SetText(text.Replace('\r', ' ').Replace('\n', ' '));
        uiManager.PushWindow(messageBox);
    }

    private void SetupImportProgressPanel()
    {
        importProgressPanel.Position = new Vector2(54, 184);
        importProgressPanel.Size = new Vector2(210, 10);
        importProgressPanel.BackgroundColor = new Color(0f, 0f, 0f, 0.75f);
        importProgressPanel.Outline.Enabled = true;
        importProgressPanel.Enabled = false;
        NativePanel.Components.Add(importProgressPanel);

        importProgressBarBg.Position = new Vector2(8, 1);
        importProgressBarBg.Size = new Vector2(194, 8);
        importProgressBarBg.BackgroundColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        importProgressBarBg.Outline.Enabled = true;
        importProgressPanel.Components.Add(importProgressBarBg);

        importProgressBarFill.Position = importProgressBarBg.Position + new Vector2(1, 1);
        importProgressBarFill.Size = new Vector2(0, importProgressBarBg.Size.y - 2);
        importProgressBarFill.BackgroundColor = Color.green;
        importProgressPanel.Components.Add(importProgressBarFill);

        importProgressText.Position = new Vector2(0, 1);
        importProgressText.Size = new Vector2(importProgressPanel.Size.x, 10);
        importProgressText.HorizontalAlignment = HorizontalAlignment.Center;
        importProgressText.TextColor = Color.black;
        importProgressText.ShadowPosition = Vector2.zero;
        importProgressPanel.Components.Add(importProgressText);
    }

    private void ShowImportProgress()
    {
        importProgressPanel.Enabled = true;
    }

    private void HideImportProgress()
    {
        importProgressPanel.Enabled = false;
    }

    private void SetImportProgress(float percent, string status)
    {
        float width = (importProgressBarBg.Size.x - 2) * Mathf.Clamp01(percent / 100f);
        importProgressBarFill.Size = new Vector2(width, importProgressBarBg.Size.y - 2);
        importProgressText.Text = string.Format("{0} {1:0}%", status, percent);
    }

    private void ShowConfirmationBox(string text, Action onSelectedYes, Action onSelectedNo)
    {
        var confirmationBox = new DaggerfallMessageBox(uiManager, this);
        confirmationBox.ParentPanel.BackgroundTexture = null;
        confirmationBox.SetText(text);
        confirmationBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
        confirmationBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
        confirmationBox.OnButtonClick += (messageBox, messageBoxButton) =>
        {
            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes) {
                onSelectedYes?.Invoke();
            }
            else {
                onSelectedNo?.Invoke();
            }
            messageBox.CancelWindow();
        };
        uiManager.PushWindow(confirmationBox);
    }

    private void ShowAllFilesAccessBox(string text)
    {
        var messageBox = new DaggerfallMessageBox(uiManager, this, true);
        messageBox.ParentPanel.BackgroundTexture = null;
        messageBox.SetText(text);
        messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
        messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
        messageBox.OnButtonClick += (sender, messageBoxButton) =>
        {
            sender.CloseWindow();

            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes)
                AndroidUtils.OpenAllFilesAccessSettings();
        };
        uiManager.PushWindow(messageBox);
    }
    void ImportMod_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        Debug.Log("importing mod");
        NativeFilePicker.FilePickedCallback filePickedCallback = new NativeFilePicker.FilePickedCallback(OnImportedModFilePicked);
        NativeFilePicker.PickFile(filePickedCallback);
    }

    void ShowModDescriptionPopUp_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        if (modSettings == null || modSettings.Length < 1)
            return;
        else if (string.IsNullOrWhiteSpace(modSettings[currentSelection].modInfo.ModDescription))
            return;

        ModDescriptionMessageBox = new DaggerfallMessageBox(uiManager, this, true);
        ModDescriptionMessageBox.AllowCancel = true;
        ModDescriptionMessageBox.ClickAnywhereToClose = true;
        ModDescriptionMessageBox.ParentPanel.BackgroundTexture = null;

        Mod mod = ModManager.Instance.GetMod(modSettings[currentSelection].modInfo.ModTitle);
        string[] modDescription = (mod.TryLocalize("Mod", "Description") ?? mod.ModInfo.ModDescription).Split('\n');
        ModDescriptionMessageBox.SetText(modDescription);
        uiManager.PushWindow(ModDescriptionMessageBox);
    }

    void ModSettingsButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        Mod mod = ModManager.Instance.GetMod(modSettings[modList.SelectedIndex].modInfo.ModTitle);
        ModSettingsWindow modSettingsWindow = new ModSettingsWindow(DaggerfallUI.UIManager, mod);
        DaggerfallUI.UIManager.PushWindow(modSettingsWindow);
    }

    void RemoveModButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
    {
        Mod mod = ModManager.Instance.GetMod(modSettings[modList.SelectedIndex].modInfo.ModTitle);
        if (mod != null)
        {
            string modTitle = mod.Title;
            string modPath = Path.Combine(mod.DirPath, mod.FileName + ".dfmod");
            string modVersion = mod.ModInfo.ModVersion;
            string modAuthor = mod.ModInfo.ModAuthor;

            var messageBox = new DaggerfallMessageBox(uiManager, this);
            messageBox.AllowCancel = true;
            messageBox.ClickAnywhereToClose = true;
            messageBox.ParentPanel.BackgroundTexture = null;
            messageBox.SetText(string.Format(ModManager.GetText("removeModConfirmation"), modTitle));
            messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
            messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
            messageBox.OnButtonClick += (sender2, button) =>
            {
                if (button == DaggerfallMessageBox.MessageBoxButtons.Yes)
                {
                    ModManager.Instance.UnloadMod(modTitle, true);
                    File.Delete(modPath);
                    RefreshButton_OnMouseClick(null, Vector2.zero);
                    AndroidUtils.RestartAndroid();
                }
            };
            uiManager.PushWindow(messageBox);
        }
    }

    void ModEnabledCheckBox_OnToggleState()
    {
        if (modSettings == null || modSettings.Length < 1)
            return;

        ModSettings ms = modSettings[modList.SelectedIndex];

        if (ms.modInfo == null)
            return;

        modSettings[modList.SelectedIndex].enabled = modEnabledCheckBox.IsChecked;
        modList.SelectedValue.textColor = modEnabledCheckBox.IsChecked ? unselectedTextColor : disabledModTextColor;
        UpdateModPanel();
    }

    void ModList_OnScroll()
    {
        modListScrollBar.ScrollIndex = modList.ScrollIndex;
    }

    void ModListScrollBar_OnScroll()
    {
        modList.ScrollIndex = modListScrollBar.ScrollIndex;
    }

    #endregion
}
