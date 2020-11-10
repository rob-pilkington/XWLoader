using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Assets.Scripts;
using Assets.Scripts.LfdReader;
using System.Linq;
using System;
using UnityEngine.UI;

public class ModelLoader : MonoBehaviour
{
    public Material ShipMaterial;
    public Material MarkingMaterial;

    private List<LoadedModel> _shipRecords;
    private int _currentRecord = 0;
    private int _currentLod = 0;
    private bool _showSpecialMarkings = true;
    private int _currentFlightGroupColorIndex = 0;

    [SerializeField] private GameObject _baseShip;
    [SerializeField] private GameObject _baseSection;
    [SerializeField] private GameObject _baseHardpoint;

    private IDictionary<string, byte[][]> _palette;
    private GameObject _shipContainer;
    private bool inSettingsMenu = true;

    [SerializeField] private Text _modelName;
    [SerializeField] private Text _modelSize;
    [SerializeField] private Text _modelSections;

    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private InputField palettePathInput;
    [SerializeField] private InputField xWingCrftResourcePathInput;
    [SerializeField] private InputField xWingCplxResourcePathInput;
    [SerializeField] private InputField tieShipResourcePathInput;
    [SerializeField] private Text settingsValidationText;

    private string paletteFileName;
    private string xWingCrftResourcePath;
    private string xWingCplxResourcePath;
    private string tieShipResourcePath;

    private const float BaseScaleFactor = 0.0244140625f;

    private static CoordinateConverter _bigCoordinateConverter = new CoordinateConverter(BaseScaleFactor * 2);
    private static CoordinateConverter _smallCoordinateConverter = new CoordinateConverter(BaseScaleFactor / 2);
    private static List<Color?> _flightGroupColors = new List<Color?>
    {
        // Use red color from palette
        null,
        // too bright colors
        Color.blue,
        Color.yellow,
        Color.green,
        // something closer to the colors we'd see in-game, but custom defined
        new Color32(152, 72, 40, byte.MaxValue), // red
        new Color32(40, 52, 120, byte.MaxValue), // blue
        new Color32(140, 104, 24, byte.MaxValue), // gold
        new Color32(40, 120, 52, byte.MaxValue) // green
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

        if (!string.IsNullOrWhiteSpace(tieShipResourcePath))
        {
            const string Source = "TIE Fighter";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(tieShipResourcePath, "SPECIES.LFD")));
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(tieShipResourcePath, "SPECIES2.LFD")));
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(tieShipResourcePath, "SPECIES3.LFD")));
        }

        var fileGroups = new Dictionary<string, List<FileToLoad>>
        {
            ["LFD"] = lfdFiles,
            ["CRFT"] = crftFiles,
            ["CPLX"] = cplxFiles
        };

        _palette = new Dictionary<string, byte[][]>();
        var xwingPalette = LoadPalette(paletteFileName);
        _palette.Add("CRFT", xwingPalette);
        _palette.Add("CPLX", xwingPalette);
        _palette.Add("SHIP", xwingPalette);
        // Different layout than X-Wing, need to map this. Using hack layout to X-Wing's palette for now.
        //_palette.Add("SHIP", LoadPalette(tiePaletteFileName)));

        _shipRecords = new List<LoadedModel>();

        foreach (var fileGroup in fileGroups)
        {
            foreach (var fileToLoad in fileGroup.Value)
            {
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
                        case "CPLX": return new CplxRecord(false);
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
                _currentFlightGroupColorIndex = 2;// _flightGroupColors.Count - 1;

            LoadShip();
        }

        if (Input.GetKeyDown(KeyCode.Equals))
        {
            if (++_currentFlightGroupColorIndex >= 3)// _flightGroupColors.Count)
                _currentFlightGroupColorIndex = 0;

            LoadShip();
        }
    }

    public void SaveButtonOnClick()
    {
        paletteFileName = palettePathInput.text;
        xWingCrftResourcePath = xWingCrftResourcePathInput.text;
        xWingCplxResourcePath = xWingCplxResourcePathInput.text;
        tieShipResourcePath = tieShipResourcePathInput.text;

        if (!string.Equals(Path.GetFileName(paletteFileName), "vga.pac", StringComparison.OrdinalIgnoreCase))
            paletteFileName = Path.Combine(paletteFileName, "vga.pac");

        settingsValidationText.text = string.Empty;

        if (!File.Exists(paletteFileName))
        {
            settingsValidationText.text = "Cannot find VGA.PAC";
            return;
        }

        if (!string.IsNullOrWhiteSpace(xWingCrftResourcePath) && !File.Exists(Path.Combine(xWingCrftResourcePath, "species.lfd")))
        {
            settingsValidationText.text = "Invalid X-Wing 93 RESOURCE folder";
            return;
        }

        if (!string.IsNullOrWhiteSpace(xWingCplxResourcePath) && !File.Exists(Path.Combine(xWingCplxResourcePath, "species.lfd")))
        {
            settingsValidationText.text = "Invalid X-Wing 94 RESOURCE folder";
            return;
        }

        if (!string.IsNullOrWhiteSpace(tieShipResourcePath) && !File.Exists(Path.Combine(tieShipResourcePath, "species.lfd")))
        {
            settingsValidationText.text = "Invalid TIE Fighter RESOURCE folder";
            return;
        }

        if (string.IsNullOrWhiteSpace(xWingCrftResourcePath) && string.IsNullOrWhiteSpace(xWingCplxResourcePath) && string.IsNullOrWhiteSpace(tieShipResourcePath))
        {
            settingsValidationText.text = "Need at least one RESOURCE path configured";
            return;
        }

        SaveSettings();

        Destroy(settingsPanel);

        inSettingsMenu = false;

        LoadModels();
    }

    void LoadSettings()
    {
        paletteFileName = PlayerPrefs.GetString("XwingPalette");
        xWingCrftResourcePath = PlayerPrefs.GetString("XwingCrftResourcePath");
        xWingCplxResourcePath = PlayerPrefs.GetString("XwingCplxResourcePath");
        tieShipResourcePath = PlayerPrefs.GetString("TieShipResourcePath");

        palettePathInput.text = paletteFileName;
        xWingCrftResourcePathInput.text = xWingCrftResourcePath;
        xWingCplxResourcePathInput.text = xWingCplxResourcePath;
        tieShipResourcePathInput.text = tieShipResourcePath;
    }

    void SaveSettings()
    {
        PlayerPrefs.SetString("XwingPalette", paletteFileName);
        PlayerPrefs.SetString("XwingCrftResourcePath", xWingCrftResourcePath);
        PlayerPrefs.SetString("XwingCplxResourcePath", xWingCplxResourcePath);
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

        var shipRecord = record.Model as ShipRecord;
        var sectionHardpoints = shipRecord != null ? shipRecord.SectionHardpoints : new HardpointRecord[0][];

        CreateMesh(record.Type, record.Name, record.Model.Sections, sectionHardpoints, isBigShip);
    }

    void CreateMesh(string recordType, string recordName, SectionRecord[] sections, HardpointRecord[][] sectionHardpoints, bool isBigShip)
    {
        Debug.Log($"Loading {recordType} {recordName}");

        var coordinateConverter = isBigShip ? _bigCoordinateConverter : _smallCoordinateConverter;

        MeshCreater meshCreater = new MeshCreater(coordinateConverter, _baseShip, _baseSection, _baseHardpoint, null, ShipMaterial, MarkingMaterial, _palette[recordType]);

        sections = FilterSections(recordType, recordName, sections);

        var disabledMarkingSectionIndices = _showSpecialMarkings ? new int[0] : GetDisabledMarkingSectionIndices(recordType, recordName);

        _shipContainer = meshCreater.CreateGameObject(sections, sectionHardpoints, recordType == "SHIP", _currentLod, _currentFlightGroupColorIndex, disabledMarkingSectionIndices);
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
        public FileToLoad(string source, string filename, bool hasWrongEndianLinerRadius = false)
        {
            Source = source;
            Filename = filename;
            HasWrongEndianLineRadius = hasWrongEndianLinerRadius;
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

    // TODO: break these out into their own palette handling classes
    public static byte[][] LoadPalette(string filename)
    {
        using (var fs = File.OpenRead(filename))
        {
            return LoadPalette(fs);
        }
    }

    public static byte[][] LoadPalette(Stream fs)
    {
        var entryCount = fs.Length / 3;
        var palette = new byte[entryCount][];

        for (var i = 0; i < entryCount; i++)
        {
            var paletteEntry = new byte[3];
            fs.Read(paletteEntry, 0, 3);
            palette[i] = paletteEntry;
        }

        return palette;
    }
}
