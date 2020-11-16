using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Assets.Scripts;
using Assets.Scripts.LfdReader;
using System.Linq;
using System;
using UnityEngine.UI;
using Assets.Scripts.Palette;

public class ModelLoader : MonoBehaviour
{
    private List<LoadedModel> _shipRecords;
    private int _currentRecord = 0;
    private int _currentLod = 0;
    private bool _showSpecialMarkings = true;
    private int _currentFlightGroupColorIndex = 0;

    [SerializeField] private GameObject _baseShip = null;
    [SerializeField] private GameObject _baseSection = null;
    [SerializeField] private GameObject _baseHardpoint = null;

    private IDictionary<string, GameObject> _baseSections;
    private IDictionary<string, IPaletteMapper> _paletteMappers;

    private GameObject _shipContainer;
    private bool inSettingsMenu = true;

    [SerializeField] private Text _modelName = null;
    [SerializeField] private Text _modelSize = null;
    [SerializeField] private Text _modelSections = null;

    [SerializeField] private GameObject settingsPanel = null;
    [SerializeField] private InputField xwPalettePathInput = null;
    [SerializeField] private InputField tiePalettePathInput = null;
    [SerializeField] private InputField xWingCrftResourcePathInput = null;
    [SerializeField] private InputField xWingCplxResourcePathInput = null;
    [SerializeField] private InputField xWingCplxWindowsResourcePathInput = null;
    [SerializeField] private InputField tieShipResourcePathInput = null;
    [SerializeField] private Text settingsValidationText = null;

    [SerializeField] private Light _light = null;

    private bool _enableLightRotation = false;

    private string xwPaletteFileName;
    private string tiePaletteFileName;
    private string xWingCrftResourcePath;
    private string xWingCplxResourcePath;
    private string xWingCplxWindowsResourcePath;
    private string tieShipResourcePath;

    private const float BaseScaleFactor = 0.0244140625f;

    private static readonly CoordinateConverter _bigCoordinateConverter = new CoordinateConverter(BaseScaleFactor * 2);
    private static readonly CoordinateConverter _smallCoordinateConverter = new CoordinateConverter(BaseScaleFactor / 2);
    private static readonly List<Color> _customFlightGroupColors = new List<Color>
    {
        new Color32(40, 120, 52, 255) // green
    };

    // Use this for initialization
    void Start()
    {
        settingsPanel.SetActive(true);
        LoadSettings();
    }

    void LoadModels()
    {
        var lfdFiles = new List<FileToLoad>();
        var crftFiles = new List<FileToLoad>();
        var cplxFiles = new List<FileToLoad>();

        if (!string.IsNullOrWhiteSpace(xWingCrftResourcePath))
        {
            const string Source = "X-Wing 93";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(xWingCrftResourcePath, "SPECIES.LFD")));
            crftFiles.Add(new FileToLoad(Source, Path.Combine(xWingCrftResourcePath, "BWING.CFT")));
        }

        if (!string.IsNullOrWhiteSpace(xWingCplxResourcePath))
        {
            const string Source = "X-Wing 94";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(xWingCplxResourcePath, "SPECIES.LFD")));
            cplxFiles.Add(new FileToLoad(Source, Path.Combine(xWingCplxResourcePath, "BWING.CFT")));
        }

        if (!string.IsNullOrWhiteSpace(xWingCplxWindowsResourcePath))
        {
            lfdFiles.Add(new FileToLoad("X-Wing 98", Path.Combine(xWingCplxWindowsResourcePath, "SPECIES.LFD"), hasWrongEndianLineRadius: true));
        }

        // Need more information
        //if (!string.IsNullOrWhiteSpace(xWingCplxMacResourcePath))
        //{
        //    lfdFiles.Add(new FileToLoad("X-Wing Mac", Path.Combine(xWingCplxMacResourcePath, "SPECIES.LFD"), true));
        //    // Is there a B-wing file?
        //}

        if (!string.IsNullOrWhiteSpace(tieShipResourcePath))
        {
            // Note: there are also additional files in RES320/RES640 folders. There are also LFD.320 files that appear to be the same as the regular files in the GOG 98 release.
            // I haven't yet found any reason to use these files instead, despite some having different file sizes.
            // (I haven't yet noted any model changes; maybe different resolution bitmaps? More research needed.)
            const string Source = "TIE Fighter";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(tieShipResourcePath, "SPECIES.LFD")));
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(tieShipResourcePath, "SPECIES2.LFD")));
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(tieShipResourcePath, "SPECIES3.LFD")));
        }

        // Need more information
        //if (!string.IsNullOrWhiteSpace(tieShipMacResourcePath))
        //{
        //    const string Source = "TIE Fighter Mac";
        //    lfdFiles.Add(new FileToLoad(source, Path.Combine(tieShipMacResourcePath, "SPECIES.LFD")));
        //    lfdFiles.Add(new FileToLoad(source, Path.Combine(tieShipMacResourcePath, "SPECIES2.LFD")));
        //    lfdFiles.Add(new FileToLoad(source, Path.Combine(tieShipMacResourcePath, "SPECIES3.LFD")));
        //}

        var fileGroups = new Dictionary<string, List<FileToLoad>>
        {
            ["LFD"] = lfdFiles,
            ["CRFT"] = crftFiles,
            ["CPLX"] = cplxFiles
        };

        // TODO: these appear in the scene; would be nice to clean up. A better solution may be to use a MaterialPropertyBlock.
        var xwingBaseSection = Instantiate(_baseSection);
        var tieFighterBaseSection = Instantiate(_baseSection);

        _baseSections = new Dictionary<string, GameObject>()
        {
            ["CRFT"] = xwingBaseSection,
            ["CPLX"] = xwingBaseSection,
            ["SHIP"] = tieFighterBaseSection
        };

        // Assumes the first material is the same as all materials in use.
        var xwingMaterial = new Material(_baseSection.GetComponentInChildren<MeshRenderer>().sharedMaterial);
        var tieFighterMaterial = new Material(_baseSection.GetComponentInChildren<MeshRenderer>().sharedMaterial);

        //foreach (var name in xwingMaterial.GetTexturePropertyNames())
        //    Debug.Log($"Texture name: {name}");

        foreach (var meshRenderer in xwingBaseSection.GetComponentsInChildren<MeshRenderer>())
            meshRenderer.sharedMaterial = xwingMaterial;

        foreach (var meshRenderer in tieFighterBaseSection.GetComponentsInChildren<MeshRenderer>())
            meshRenderer.sharedMaterial = tieFighterMaterial;

        var xwingPaletteMapper = new XWingPaletteMapper(PaletteMapper.LoadPalette(xwPaletteFileName), _customFlightGroupColors.ToArray());
        var tieFighterPaletteMapper = new TieFighterPaletteMapper(PaletteMapper.LoadPalette(tiePaletteFileName), _customFlightGroupColors.ToArray());

        _paletteMappers = new Dictionary<string, IPaletteMapper>
        {
            ["CRFT"] = xwingPaletteMapper,
            ["CPLX"] = xwingPaletteMapper,
            ["SHIP"] = tieFighterPaletteMapper
        };

        var xwingTexture = xwingPaletteMapper.GeneratePaletteTexture();
        var xwingSpecularTexture = xwingPaletteMapper.GenerateSpecularMap();
        var xwingEmissionTexture = xwingPaletteMapper.GenerateEmissionMap();

        xwingMaterial.SetTexture("_MainTex", xwingTexture);
        //xwingMaterial.SetTexture("_MetallicGlossMap", xwingSpecularTexture);
        xwingMaterial.SetTexture("_SpecGlossMap", xwingSpecularTexture);
        xwingMaterial.SetTexture("_EmissionMap", xwingEmissionTexture);

        xwingMaterial.EnableKeyword("_METALLICGLOSSMAP");
        xwingMaterial.EnableKeyword("_SPECGLOSSMAP");
        xwingMaterial.EnableKeyword("_EMISSION");

        var tieFighterTexture = tieFighterPaletteMapper.GeneratePaletteTexture();
        var tieFighterSpecularTexture = tieFighterPaletteMapper.GenerateSpecularMap();
        var tieFighterEmissionTexture = xwingPaletteMapper.GenerateEmissionMap();

        tieFighterMaterial.SetTexture("_MainTex", tieFighterTexture);
        //tieFighterMaterial.SetTexture("_MetallicGlossMap", xwingSpecularTexture);
        tieFighterMaterial.SetTexture("_SpecGlossMap", tieFighterSpecularTexture);
        tieFighterMaterial.SetTexture("_EmissionMap", tieFighterEmissionTexture);

        tieFighterMaterial.EnableKeyword("_METALLICGLOSSMAP");
        tieFighterMaterial.EnableKeyword("_SPECGLOSSMAP");
        tieFighterMaterial.EnableKeyword("_EMISSION");

        _shipRecords = new List<LoadedModel>();

        foreach (var fileGroup in fileGroups)
        {
            foreach (var fileToLoad in fileGroup.Value)
            {
                // The B-Wing standalone file will not exist if the appropriate expansion is not installed).
                if (!File.Exists(fileToLoad.Filename) && fileGroup.Key == "CRFT")
                    continue;

                using (var fs = File.OpenRead(fileToLoad.Filename))
                {
                    // Load an LFD file and all of the models within it
                    if (fileGroup.Key == "LFD")
                    {
                        var reader = new LfdReader();

                        reader.Read(fs, fileToLoad.HasWrongEndianLineRadius);

                        foreach (var record in reader.Records)
                        {
                            var ship = record.Value;
                            if (ship.RecordType == "CRFT" || ship.RecordType == "CPLX" || ship.RecordType == "SHIP")
                                _shipRecords.Add(new LoadedModel(fileToLoad.Source, ship.RecordType, ship.RecordName, (ICraft)ship));
                        }
                    }
                    else // Just load a specific model
                    {
                        var reader = GetRecordReader(fileGroup.Key);

                        reader.Read(fs, fileGroup.Key, fileToLoad.Filename);

                        _shipRecords.Add(new LoadedModel(
                            fileToLoad.Source,
                            fileGroup.Key,
                            Path.GetFileNameWithoutExtension(fileToLoad.Filename),
                            (ICraft)reader));
                    }
                }

                LfdRecord GetRecordReader(string type)
                {
                    switch (type)
                    {
                        case "CRFT": return new CrftRecord();
                        case "CPLX": return new CplxRecord(fileToLoad.HasWrongEndianLineRadius);
                        case "SHIP": return new ShipRecord();
                        default: throw new NotSupportedException($"Unknown file type: {type}");
                    }
                }
            }
        }

        LoadShip();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Escape))
            Application.Quit();

        if (inSettingsMenu) // everything after this is controls
            return;

        var cameraTransform = Camera.main.transform;

        var delta = 100 * Time.deltaTime;

        if (Input.GetKey(KeyCode.W))
            cameraTransform.Translate(Vector3.forward * delta);

        if (Input.GetKey(KeyCode.S))
            cameraTransform.Translate(Vector3.back * delta);

        if (Input.GetKey(KeyCode.D))
            cameraTransform.Translate(Vector3.right * delta);

        if (Input.GetKey(KeyCode.A))
            cameraTransform.Translate(Vector3.left * delta);

        if (Input.GetKey(KeyCode.LeftControl))
            cameraTransform.Translate(Vector3.down * delta);

        if (Input.GetKey(KeyCode.Space))
            cameraTransform.Translate(Vector3.up * delta);

        if (Input.GetKey(KeyCode.Q))
            cameraTransform.Rotate(Vector3.forward, 50 * Time.deltaTime);

        if (Input.GetKey(KeyCode.E))
            cameraTransform.Rotate(Vector3.forward, -50 * Time.deltaTime);

        if (Input.GetKey(KeyCode.UpArrow))
            cameraTransform.Rotate(Vector3.right, 50 * Time.deltaTime);

        if (Input.GetKey(KeyCode.DownArrow))
            cameraTransform.Rotate(Vector3.right, -50 * Time.deltaTime);

        if (Input.GetKey(KeyCode.LeftArrow))
            cameraTransform.Rotate(Vector3.up, -50 * Time.deltaTime);

        if (Input.GetKey(KeyCode.RightArrow))
            cameraTransform.Rotate(Vector3.up, 50 * Time.deltaTime);

        if (Input.GetKeyDown(KeyCode.PageUp))
        {
            // TODO: rewrite this with better organization for the sources.
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                if (_shipRecords.Select(x => x.Source).Distinct().Count() > 1)
                {
                    var currentSource = _shipRecords[_currentRecord].Source;

                    // Find the last record for the current source.
                    while (_shipRecords[_currentRecord].Source == currentSource)
                        ChangeRecord();

                    // Find the first record for the destination source.
                    var destinationSource = _shipRecords[_currentRecord].Source;
                    while (_currentRecord > 0 && _shipRecords[_currentRecord - 1].Source == destinationSource)
                        ChangeRecord();
                }
            }
            else
            {
                ChangeRecord();
            }

            _currentLod = 0;

            LoadShip();

            void ChangeRecord()
            {
                if (--_currentRecord < 0)
                    _currentRecord = _shipRecords.Count - 1;
            }
        }

        if (Input.GetKeyDown(KeyCode.PageDown))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                if (_shipRecords.Select(x => x.Source).Distinct().Count() > 1)
                {
                    var currentSource = _shipRecords[_currentRecord].Source;
                    while (_shipRecords[_currentRecord].Source == currentSource)
                        ChangeRecord();
                }
            }
            else
            {
                ChangeRecord();
            }

            _currentLod = 0;

            LoadShip();

            void ChangeRecord()
            {
                if (++_currentRecord >= _shipRecords.Count)
                    _currentRecord = 0;
            }
        }

        if (Input.GetKeyDown(KeyCode.LeftBracket))
        {
            if (--_currentLod < 0)
                _currentLod = 0;

            LoadShip();
        }

        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            var maxLod = _shipRecords[_currentRecord].Model.Sections.Select(x => x.LodRecords.Count).Max();

            if (++_currentLod >= maxLod)
                _currentLod = maxLod - 1;

            LoadShip();
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            _showSpecialMarkings = !_showSpecialMarkings;

            LoadShip();
        }

        if (Input.GetKeyDown(KeyCode.Minus))
        {
            if (--_currentFlightGroupColorIndex < 0)
                _currentFlightGroupColorIndex = _customFlightGroupColors.Count + 2; // 3 regular colors + custom colors

            LoadShip();
        }

        if (Input.GetKeyDown(KeyCode.Equals))
        {
            if (++_currentFlightGroupColorIndex >= _customFlightGroupColors.Count + 3) // 3 regular colors + custom colors
                _currentFlightGroupColorIndex = 0;

            LoadShip();
        }

        if (Input.GetKeyDown(KeyCode.Backslash))
        {
            var meshRenderers = _shipContainer.transform.GetComponentsInChildren<MeshRenderer>()
                .Where(c => c.name.StartsWith("Hardpoint", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var meshRenderer in meshRenderers)
                meshRenderer.enabled = !meshRenderer.enabled;
        }

        if (Input.GetKeyDown(KeyCode.L))
            _enableLightRotation = !_enableLightRotation;
    }

    private void FixedUpdate()
    {
        if (_enableLightRotation)
            _light.transform.Rotate(Vector3.up, 90 * Time.fixedDeltaTime, Space.World);
    }

    public void SaveButtonOnClick()
    {
        xwPaletteFileName = xwPalettePathInput.text;
        tiePaletteFileName = tiePalettePathInput.text;
        xWingCrftResourcePath = xWingCrftResourcePathInput.text;
        xWingCplxResourcePath = xWingCplxResourcePathInput.text;
        xWingCplxWindowsResourcePath = xWingCplxWindowsResourcePathInput.text;
        tieShipResourcePath = tieShipResourcePathInput.text;

        if (!string.IsNullOrWhiteSpace(xwPaletteFileName) && !string.Equals(Path.GetFileName(xwPaletteFileName), "vga.pac", StringComparison.OrdinalIgnoreCase))
            xwPaletteFileName = Path.Combine(xwPaletteFileName, "vga.pac");

        if (!string.IsNullOrWhiteSpace(tiePaletteFileName) && !string.Equals(Path.GetFileName(tiePaletteFileName), "vga.pac", StringComparison.OrdinalIgnoreCase))
            tiePaletteFileName = Path.Combine(tiePaletteFileName, "vga.pac");

        settingsValidationText.text = string.Empty;

        var isXwPaletteProvided = false;
        var isTiePaletteProvided = false;
        var isXwingResourceProvided = false;
        var isTieResourceProvided = false;

        var validationMessage = ValidatePaths();
        if (validationMessage != string.Empty)
        {
            settingsValidationText.text = validationMessage;
            return;
        }

        if (!isXwingResourceProvided && !isTieResourceProvided)
        {
            settingsValidationText.text = "Need at least one RESOURCE path configured";
            return;
        }

        if (isXwingResourceProvided && !isXwPaletteProvided)
        {
            settingsValidationText.text = "Need an X-Wing VGA.PAC configured in order to display X-Wing models";
            return;
        }

        if (isTieResourceProvided && !isTiePaletteProvided)
        {
            settingsValidationText.text = "Need a TIE Fighter VGA.PAC configured in order to display TIE Fighter models";
            return;
        }

        SaveSettings();

        Destroy(settingsPanel);

        inSettingsMenu = false;

        LoadModels();

        string ValidatePaths()
        {
            if (!ValidateFileExists(xwPaletteFileName, ref isXwPaletteProvided))
                return "Cannot find X-Wing VGA.PAC";

            if (!ValidateFileExists(tiePaletteFileName, ref isTiePaletteProvided))
                return "Cannot find TIE Fighter VGA.PAC";

            if (!ValidatePath(xWingCrftResourcePath, ref isXwingResourceProvided, "species.lfd"))
                return "Invalid X-Wing 93 RESOURCE folder";

            if (!ValidatePath(xWingCplxResourcePath, ref isXwingResourceProvided, "species.lfd", "bwing.cft"))
                return "Invalid X-Wing 94 RESOURCE folder";

            if (!ValidatePath(xWingCplxWindowsResourcePath, ref isXwingResourceProvided, "species.lfd"))
                return "Invalid X-Wing 98 RESOURCE folder";

            if (!ValidatePath(tieShipResourcePath, ref isTieResourceProvided, "species.lfd", "species2.lfd", "species3.lfd"))
            {
                // Original XW only had species.lfd
                if (!ValidatePath(tieShipResourcePath, ref isTieResourceProvided, "species.lfd"))
                    return "Invalid TIE Fighter RESOURCE folder";
            }

            return string.Empty;
        }

        bool ValidateFileExists(string filename, ref bool isProvided)
        {
            if (!string.IsNullOrWhiteSpace(filename))
            {
                isProvided = true;

                if (!File.Exists(filename))
                    return false;
            }

            return true;
        }

        bool ValidatePath(string path, ref bool isProvided, params string[] filenamesToCheck)
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

    void LoadSettings()
    {
        xwPaletteFileName = PlayerPrefs.GetString("XwingPalette");
        tiePaletteFileName = PlayerPrefs.GetString("TiePalette");
        xWingCrftResourcePath = PlayerPrefs.GetString("XwingCrftResourcePath");
        xWingCplxResourcePath = PlayerPrefs.GetString("XwingCplxResourcePath");
        xWingCplxWindowsResourcePath = PlayerPrefs.GetString("XwingCplxWindowsResourcePath");
        tieShipResourcePath = PlayerPrefs.GetString("TieShipResourcePath");

        xwPalettePathInput.text = xwPaletteFileName;
        tiePalettePathInput.text = tiePaletteFileName;
        xWingCrftResourcePathInput.text = xWingCrftResourcePath;
        xWingCplxResourcePathInput.text = xWingCplxResourcePath;
        xWingCplxWindowsResourcePathInput.text = xWingCplxWindowsResourcePath;
        tieShipResourcePathInput.text = tieShipResourcePath;
    }

    void SaveSettings()
    {
        PlayerPrefs.SetString("XwingPalette", xwPaletteFileName);
        PlayerPrefs.SetString("TiePalette", tiePaletteFileName);
        PlayerPrefs.SetString("XwingCrftResourcePath", xWingCrftResourcePath);
        PlayerPrefs.SetString("XwingCplxResourcePath", xWingCplxResourcePath);
        PlayerPrefs.SetString("XwingCplxWindowsResourcePath", xWingCplxWindowsResourcePath);
        PlayerPrefs.SetString("TieShipResourcePath", tieShipResourcePath);
        PlayerPrefs.Save();
    }

    void LoadShip()
    {
        if (_shipContainer != null)
            Destroy(_shipContainer);

        var record = _shipRecords[_currentRecord];

        var isBigShip = IsBigShip(record.Type, record.Name);

        UpdateUiLabels(record, isBigShip);

        var sectionHardpoints = record.Model is ShipRecord shipRecord ? shipRecord.SectionHardpoints : new HardpointRecord[0][];

        CreateMesh(record.Type, record.Name, record.Model.Sections, sectionHardpoints, isBigShip);
    }

    void CreateMesh(string recordType, string recordName, SectionRecord[] sections, HardpointRecord[][] sectionHardpoints, bool isBigShip)
    {
        Debug.Log($"Loading {recordType} {recordName}");

        var coordinateConverter = isBigShip ? _bigCoordinateConverter : _smallCoordinateConverter;

        MeshCreater meshCreater = new MeshCreater(coordinateConverter, _baseShip, _baseSections[recordType], _baseHardpoint, null, _paletteMappers[recordType]);

        sections = FilterSections(recordType, recordName, sections);

        var disabledMarkingSectionIndices = _showSpecialMarkings ? new int[0] : GetDisabledMarkingSectionIndices(recordType, recordName);

        _shipContainer = meshCreater.CreateGameObject(sections, sectionHardpoints, _currentLod, _currentFlightGroupColorIndex, disabledMarkingSectionIndices);
    }

    private static SectionRecord[] FilterSections(string recordType, string recordName, SectionRecord[] sections)
    {
        if (recordType == "CRFT" || recordType == "CPLX")
        {
            // Normally, each section has its own set of LODs (you might see, e.g., one section change LOD while the others stay the same if
            // its threshold is met but the thresholds for the other sections aren't).
            // These two big ships have a special case where the last section is used to replace all the other sections.
            // Makes sense: at a certain distance treating it as a single section would be quicker than handling the separate sections.
            // We'll ignore them for now.
            switch (recordName)
            {
                case "STARDEST":
                case "ISDCD92":
                    sections = sections.Take(13).ToArray();
                    break;

                case "CALAMARI":
                case "CALCD9":
                    sections = sections.Take(2).ToArray();
                    break;
            }
        }

        return sections;
    }

    private static int[] GetDisabledMarkingSectionIndices(string recordType, string recordName)
    {
        if (recordType == "CRFT" && recordName == "SHUTTLE")
            return new int[] { 3 };

        if ((recordType == "CPLX" || recordType == "SHIP") && recordName == "SHUTTLE")
            return new int[] { 6 };

        return new int[0];
    }

    private static bool IsBigShip(string recordType, string recordName)
    {
        // TODO: all of this special case stuff should be encapsulated somewhere else.
        var bigModels = new Dictionary<string, HashSet<string>>
        {
            ["CRFT"] = new HashSet<string> { "STARDEST", "CALAMARI" },
            ["CPLX"] = new HashSet<string> { "CALCD9", "ISDCD92" },
            ["SHIP"] = new HashSet<string> { "ISD", "CAL" }
        };

        if (bigModels.TryGetValue(recordType, out var names))
        {
            if (names.Contains(recordName))
                return true;
        }

        return false;
    }

    private void UpdateUiLabels(LoadedModel loadedModel, bool isBigShip)
    {
        _modelName.text = $"{loadedModel.Source}: {loadedModel.Type} {loadedModel.Name}";

        var sections = FilterSections(loadedModel.Type, loadedModel.Name, loadedModel.Model.Sections);

        var boundingBoxes = sections.Select(x => x.LodRecords[0].BoundingBox1)
            .Union(sections.Select(x => x.LodRecords[0].BoundingBox2));

        var minimum = new Vector3(boundingBoxes.Min(x => x.x), boundingBoxes.Min(x => x.y), boundingBoxes.Min(x => x.z));
        var maximum = new Vector3(boundingBoxes.Max(x => x.x), boundingBoxes.Max(x => x.y), boundingBoxes.Max(x => x.z));

        var scaleFactor = isBigShip ? BaseScaleFactor * 2 : BaseScaleFactor / 2;

        var size = maximum - minimum;

        _modelSize.text = $"Size: {size} (in meters: {size * scaleFactor})";

        var actualSectionCount = loadedModel.Model.Sections.Length;
        _modelSections.text = $"Sections: {sections.Length}" + (sections.Length != actualSectionCount ? $" ({actualSectionCount})" : "");
    }

    private class FileToLoad
    {
        public FileToLoad(string source, string filename, bool hasWrongEndianLineRadius = false)
        {
            Source = source;
            Filename = filename;
            HasWrongEndianLineRadius = hasWrongEndianLineRadius;
        }

        public string Source { get; private set; }
        public string Filename { get; private set; }
        public bool HasWrongEndianLineRadius { get; private set; }
    }

    private class LoadedModel
    {
        public LoadedModel(string source, string type, string name, ICraft model)
        {
            Source = source;
            Type = type;
            Name = name;
            Model = model;
        }

        public string Source { get; private set; }
        public string Type { get; private set; }
        public string Name { get; private set; }
        public ICraft Model { get; private set; }
    }
}
