using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class SettingsController : MonoBehaviour
{
    [SerializeField] private GameObject _settingsPanel = null;
    [SerializeField] private InputField _xwPalettePathInput = null;
    [SerializeField] private InputField _tiePalettePathInput = null;
    [SerializeField] private InputField _xWingCrftResourcePathInput = null;
    [SerializeField] private InputField _xWingCplxResourcePathInput = null;
    [SerializeField] private InputField _xWingCplxWindowsResourcePathInput = null;
    [SerializeField] private InputField _tieShipResourcePathInput = null;
    [SerializeField] private Text _settingsValidationText = null;

    public string XwPaletteFileName { get; private set; }
    public string TiePaletteFileName { get; private set; }
    public string XWingCrftResourcePath { get; private set; }
    public string XWingCplxResourcePath { get; private set; }
    public string XWingCplxWindowsResourcePath { get; private set; }
    public string TieShipResourcePath { get; private set; }

    public bool InSettingsMenu { get; private set; }

    public event EventHandler SettingsLoaded;
    public event EventHandler Canceled;

    protected virtual void OnSettingsLoaded() => SettingsLoaded?.Invoke(this, EventArgs.Empty);

    protected virtual void OnCanceled() => Canceled?.Invoke(this, EventArgs.Empty);
    
    public void ShowSettingsWindow()
    {
        _settingsPanel.SetActive(true);
        InSettingsMenu = true;
    }

    private void HideSettingsWindow()
    {
        _settingsPanel.SetActive(false);
        InSettingsMenu = false;
    }

    public void SaveButtonOnClick()
    {
        XwPaletteFileName = _xwPalettePathInput.text;
        TiePaletteFileName = _tiePalettePathInput.text;
        XWingCrftResourcePath = _xWingCrftResourcePathInput.text;
        XWingCplxResourcePath = _xWingCplxResourcePathInput.text;
        XWingCplxWindowsResourcePath = _xWingCplxWindowsResourcePathInput.text;
        TieShipResourcePath = _tieShipResourcePathInput.text;

        if (!string.IsNullOrWhiteSpace(XwPaletteFileName) && !string.Equals(Path.GetFileName(XwPaletteFileName), "vga.pac", StringComparison.OrdinalIgnoreCase))
            XwPaletteFileName = Path.Combine(XwPaletteFileName, "vga.pac");

        if (!string.IsNullOrWhiteSpace(TiePaletteFileName) && !string.Equals(Path.GetFileName(TiePaletteFileName), "vga.pac", StringComparison.OrdinalIgnoreCase))
            TiePaletteFileName = Path.Combine(TiePaletteFileName, "vga.pac");

        _settingsValidationText.text = string.Empty;

        var validationMessage = ValidateSettings();
        if (validationMessage != string.Empty)
        {
            _settingsValidationText.text = validationMessage;
            return;
        }

        SaveSettings();

        HideSettingsWindow();

        OnSettingsLoaded();
    }

    public void LoadSettings()
    {
        XwPaletteFileName = PlayerPrefs.GetString("XwingPalette");
        TiePaletteFileName = PlayerPrefs.GetString("TiePalette");
        XWingCrftResourcePath = PlayerPrefs.GetString("XwingCrftResourcePath");
        XWingCplxResourcePath = PlayerPrefs.GetString("XwingCplxResourcePath");
        XWingCplxWindowsResourcePath = PlayerPrefs.GetString("XwingCplxWindowsResourcePath");
        TieShipResourcePath = PlayerPrefs.GetString("TieShipResourcePath");

        _xwPalettePathInput.text = XwPaletteFileName;
        _tiePalettePathInput.text = TiePaletteFileName;
        _xWingCrftResourcePathInput.text = XWingCrftResourcePath;
        _xWingCplxResourcePathInput.text = XWingCplxResourcePath;
        _xWingCplxWindowsResourcePathInput.text = XWingCplxWindowsResourcePath;
        _tieShipResourcePathInput.text = TieShipResourcePath;

        _settingsValidationText.text = string.Empty;
        var validationMessage = ValidateSettings();
        if (validationMessage != string.Empty)
        {
            _settingsValidationText.text = validationMessage;
            ShowSettingsWindow();
            return;
        }

        OnSettingsLoaded();
    }

    private void SaveSettings()
    {
        PlayerPrefs.SetString("XwingPalette", XwPaletteFileName);
        PlayerPrefs.SetString("TiePalette", TiePaletteFileName);
        PlayerPrefs.SetString("XwingCrftResourcePath", XWingCrftResourcePath);
        PlayerPrefs.SetString("XwingCplxResourcePath", XWingCplxResourcePath);
        PlayerPrefs.SetString("XwingCplxWindowsResourcePath", XWingCplxWindowsResourcePath);
        PlayerPrefs.SetString("TieShipResourcePath", TieShipResourcePath);
        PlayerPrefs.Save();
    }

    private string ValidateSettings()
    {
        var isXwPaletteProvided = false;
        var isTiePaletteProvided = false;
        var isXwingResourceProvided = false;
        var isTieResourceProvided = false;

        var validationMessage = ValidatePaths();
        if (validationMessage != string.Empty)
            return validationMessage;

        if (!isXwingResourceProvided && !isTieResourceProvided)
            return "Need at least one RESOURCE path configured";

        if (isXwingResourceProvided && !isXwPaletteProvided)
            return "Need an X-Wing VGA.PAC configured in order to display X-Wing models";

        if (isTieResourceProvided && !isTiePaletteProvided)
            return "Need a TIE Fighter VGA.PAC configured in order to display TIE Fighter models";

        return string.Empty;

        string ValidatePaths()
        {
            if (!ValidateFileExists(XwPaletteFileName, ref isXwPaletteProvided))
                return "Cannot find X-Wing VGA.PAC";

            if (!ValidateFileExists(TiePaletteFileName, ref isTiePaletteProvided))
                return "Cannot find TIE Fighter VGA.PAC";

            if (!ValidatePath(XWingCrftResourcePath, ref isXwingResourceProvided, "species.lfd"))
                return "Invalid X-Wing 93 RESOURCE folder";

            if (!ValidatePath(XWingCplxResourcePath, ref isXwingResourceProvided, "species.lfd", "bwing.cft"))
                return "Invalid X-Wing 94 RESOURCE folder";

            if (!ValidatePath(XWingCplxWindowsResourcePath, ref isXwingResourceProvided, "species.lfd"))
                return "Invalid X-Wing 98 RESOURCE folder";

            if (!ValidatePath(TieShipResourcePath, ref isTieResourceProvided, "species.lfd", "species2.lfd", "species3.lfd"))
            {
                // Original TF only had species.lfd
                if (!ValidatePath(TieShipResourcePath, ref isTieResourceProvided, "species.lfd"))
                    return "Invalid TIE Fighter RESOURCE folder";
            }

            return string.Empty;
        }

        static bool ValidateFileExists(string filename, ref bool isProvided)
        {
            if (!string.IsNullOrWhiteSpace(filename))
            {
                isProvided = true;

                if (!File.Exists(filename))
                    return false;
            }

            return true;
        }

        static bool ValidatePath(string path, ref bool isProvided, params string[] filenamesToCheck)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                isProvided = true;

                if (!Directory.Exists(path))
                    return false;

                foreach (var filename in filenamesToCheck)
                    if (!File.Exists(Path.Combine(path, filename)))
                        return false;
            }

            return true;
        }
    }
}
