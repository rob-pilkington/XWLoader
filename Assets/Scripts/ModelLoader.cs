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

    [SerializeField] private SettingsController _settingsController = null;

    private IDictionary<string, MaterialPropertyBlock> _materialPropertyBlocks;
    private IDictionary<string, IPaletteMapper> _paletteMappers;

    private GameObject _shipContainer;

    [SerializeField] private Text _modelName = null;
    [SerializeField] private Text _modelSize = null;
    [SerializeField] private Text _modelSections = null;

    [SerializeField] private Light _light = null;

    private Vector3 _rotationOrigin = Vector3.zero;
    private float _rotationOriginDistance;
    private bool _enableLightRotation = false;

    private const float BaseScaleFactor = 0.0244140625f;

    private static readonly CoordinateConverter _bigCoordinateConverter = new CoordinateConverter(BaseScaleFactor * 2);
    private static readonly CoordinateConverter _smallCoordinateConverter = new CoordinateConverter(BaseScaleFactor / 2);
    private static readonly List<Color> _customFlightGroupColors = new List<Color>
    {
        new Color32(40, 120, 52, 255) // green
    };

    private void Start()
    {
        _rotationOriginDistance = Vector3.Distance(Camera.main.transform.position, _rotationOrigin);
        _settingsController.SettingsLoaded += SettingsController_SettingsLoaded;
        _settingsController.LoadSettings();
    }

    private void SettingsController_SettingsLoaded(object sender, EventArgs e) => LoadModels();

    public void SettingsButtonOnClick() => _settingsController.ShowSettingsWindow();

    private void LoadModels()
    {
        var lfdFiles = new List<FileToLoad>();
        var crftFiles = new List<FileToLoad>();
        var cplxFiles = new List<FileToLoad>();

        if (!string.IsNullOrWhiteSpace(_settingsController.XWingCrftResourcePath))
        {
            const string Source = "X-Wing 93";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.XWingCrftResourcePath, "SPECIES.LFD")));
            crftFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.XWingCrftResourcePath, "BWING.CFT")));
        }

        if (!string.IsNullOrWhiteSpace(_settingsController.XWingCplxResourcePath))
        {
            const string Source = "X-Wing 94";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.XWingCplxResourcePath, "SPECIES.LFD")));
            cplxFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.XWingCplxResourcePath, "BWING.CFT")));
        }

        if (!string.IsNullOrWhiteSpace(_settingsController.XWingCplxWindowsResourcePath))
        {
            lfdFiles.Add(new FileToLoad("X-Wing 98", Path.Combine(_settingsController.XWingCplxWindowsResourcePath, "SPECIES.LFD"), hasWrongEndianLineRadius: true));
        }

        // Need more information
        //if (!string.IsNullOrWhiteSpace(_settingsController.XWingCplxMacResourcePath))
        //{
        //    lfdFiles.Add(new FileToLoad("X-Wing Mac", Path.Combine(_settingsController.XWingCplxMacResourcePath, "SPECIES.LFD"), true));
        //    // Is there a B-wing file?
        //}

        if (!string.IsNullOrWhiteSpace(_settingsController.TieShipResourcePath))
        {
            // Note: there are also additional files in RES320/RES640 folders. There are also LFD.320 files that appear to be the same as the regular files in the GOG 98 release.
            // I haven't yet found any reason to use these files instead, despite some having different file sizes.
            // (I haven't yet noted any model changes; maybe different resolution bitmaps? More research needed.)
            const string Source = "TIE Fighter";
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.TieShipResourcePath, "SPECIES.LFD")));
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.TieShipResourcePath, "SPECIES2.LFD")));
            lfdFiles.Add(new FileToLoad(Source, Path.Combine(_settingsController.TieShipResourcePath, "SPECIES3.LFD")));
        }

        // Need more information
        //if (!string.IsNullOrWhiteSpace(_settingsController.TieShipMacResourcePath))
        //{
        //    const string Source = "TIE Fighter Mac";
        //    lfdFiles.Add(new FileToLoad(source, Path.Combine(_settingsController.TieShipMacResourcePath, "SPECIES.LFD")));
        //    lfdFiles.Add(new FileToLoad(source, Path.Combine(_settingsController.TieShipMacResourcePath, "SPECIES2.LFD")));
        //    lfdFiles.Add(new FileToLoad(source, Path.Combine(_settingsController.TieShipMacResourcePath, "SPECIES3.LFD")));
        //}

        var fileGroups = new Dictionary<string, List<FileToLoad>>
        {
            ["LFD"] = lfdFiles,
            ["CRFT"] = crftFiles,
            ["CPLX"] = cplxFiles
        };

        var xwingPaletteMapper = !string.IsNullOrWhiteSpace(_settingsController.XwPaletteFileName) ? new XWingPaletteMapper(PaletteMapper.LoadPalette(_settingsController.XwPaletteFileName), _customFlightGroupColors.ToArray()) : null;
        var tieFighterPaletteMapper = !string.IsNullOrWhiteSpace(_settingsController.TiePaletteFileName) ? new TieFighterPaletteMapper(PaletteMapper.LoadPalette(_settingsController.TiePaletteFileName), _customFlightGroupColors.ToArray()) : null;

        _paletteMappers = new Dictionary<string, IPaletteMapper>
        {
            ["CRFT"] = xwingPaletteMapper,
            ["CPLX"] = xwingPaletteMapper,
            ["SHIP"] = tieFighterPaletteMapper
        };

        var xwingMaterialPropertyBlock = new MaterialPropertyBlock();
        var tieFighterMaterialPropertyBlock = new MaterialPropertyBlock();

        // Assumes the first material is the same as all materials in use.
        var material = _baseSection.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        material.EnableKeyword("_METALLICGLOSSMAP");
        material.EnableKeyword("_SPECGLOSSMAP");
        material.EnableKeyword("_EMISSION");

        _materialPropertyBlocks = new Dictionary<string, MaterialPropertyBlock>()
        {
            ["CRFT"] = xwingMaterialPropertyBlock,
            ["CPLX"] = xwingMaterialPropertyBlock,
            ["SHIP"] = tieFighterMaterialPropertyBlock
        };

        if (xwingPaletteMapper != null) SetupMaterialPropertyBlock(xwingMaterialPropertyBlock, xwingPaletteMapper);
        if (tieFighterPaletteMapper != null) SetupMaterialPropertyBlock(tieFighterMaterialPropertyBlock, tieFighterPaletteMapper);

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

                LfdRecord GetRecordReader(string type) => type switch
                {
                    "CRFT" => new CrftRecord(),
                    "CPLX" => new CplxRecord(fileToLoad.HasWrongEndianLineRadius),
                    "SHIP" => new ShipRecord(),
                    _ => throw new NotSupportedException($"Unknown file type: {type}"),
                };
            }
        }

        _currentRecord = 0;
        LoadShip();
    }

    private static void SetupMaterialPropertyBlock(MaterialPropertyBlock materialPropertyBlock, IPaletteMapper paletteMapper)
    {
        var texture = paletteMapper.GeneratePaletteTexture();
        var specularTexture = paletteMapper.GenerateSpecularMap();
        var emissionTexture = paletteMapper.GenerateEmissionMap();

        materialPropertyBlock.SetTexture("_MainTex", texture);
        //materialPropertyBlock.SetTexture("_MetallicGlossMap", specularTexture);
        materialPropertyBlock.SetTexture("_SpecGlossMap", specularTexture);
        materialPropertyBlock.SetTexture("_EmissionMap", emissionTexture);
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.Escape))
            Application.Quit();

        if (_settingsController.InSettingsMenu) // everything after this is controls
            return;

        var cameraTransform = Camera.main.transform;

        var deltaTranslate = 100 * Time.deltaTime;
        var deltaRotate = 50 * Time.deltaTime;

        if (Input.GetMouseButton(0))
        {
            cameraTransform.RotateAround(_rotationOrigin, Vector3.up, Input.GetAxis("Mouse X") * 5);
            cameraTransform.RotateAround(_rotationOrigin, cameraTransform.right, -Input.GetAxis("Mouse Y") * 5);
        }
        else if (Input.GetMouseButton(2))
        {
            TranslateCamera(Vector3.right * -Input.GetAxis("Mouse X"));
            TranslateCamera(Vector3.up * -Input.GetAxis("Mouse Y"));
        }

        if (Input.mouseScrollDelta.y != 0)
        {
            if (_rotationOriginDistance >= 1 || Input.mouseScrollDelta.y < 0) // prevent moving closer than 1 unit from center
            {
                var amountToTravel = _rotationOriginDistance * 0.25f * Input.mouseScrollDelta.y;
                cameraTransform.position = Vector3.MoveTowards(cameraTransform.position, _rotationOrigin, amountToTravel);
                _rotationOriginDistance -= amountToTravel;
            }
        }

        if (Input.GetKey(KeyCode.W))
            TranslateCamera(Vector3.forward * deltaTranslate);

        if (Input.GetKey(KeyCode.S))
            TranslateCamera(Vector3.back * deltaTranslate);

        if (Input.GetKey(KeyCode.D))
            TranslateCamera(Vector3.right * deltaTranslate);

        if (Input.GetKey(KeyCode.A))
            TranslateCamera(Vector3.left * deltaTranslate);

        if (Input.GetKey(KeyCode.LeftControl))
            TranslateCamera(Vector3.down * deltaTranslate);

        if (Input.GetKey(KeyCode.Space))
            TranslateCamera(Vector3.up * deltaTranslate);

        if (Input.GetKey(KeyCode.Q))
            RotateCamera(Vector3.forward, deltaRotate);

        if (Input.GetKey(KeyCode.E))
            RotateCamera(Vector3.forward, -deltaRotate);

        if (Input.GetKey(KeyCode.UpArrow))
            RotateCamera(Vector3.right, deltaRotate);

        if (Input.GetKey(KeyCode.DownArrow))
            RotateCamera(Vector3.right, -deltaRotate);

        if (Input.GetKey(KeyCode.LeftArrow))
            RotateCamera(Vector3.up, -deltaRotate);

        if (Input.GetKey(KeyCode.RightArrow))
            RotateCamera(Vector3.up, deltaRotate);

        void TranslateCamera(Vector3 translation)
        {
            cameraTransform.Translate(translation);
            RecalculateRotationOrigin();
        }

        void RotateCamera(Vector3 axis, float angle)
        {
            cameraTransform.Rotate(axis, angle);
            RecalculateRotationOrigin();
        }

        void RecalculateRotationOrigin() =>_rotationOrigin = cameraTransform.position + cameraTransform.forward * _rotationOriginDistance;

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

    private void LoadShip()
    {
        if (_shipContainer != null)
            Destroy(_shipContainer);

        var record = _shipRecords[_currentRecord];

        var isBigShip = IsBigShip(record.Type, record.Name);

        UpdateUiLabels(record, isBigShip);

        var sectionHardpoints = record.Model is ShipRecord shipRecord ? shipRecord.SectionHardpoints : new HardpointRecord[0][];

        CreateMesh(record.Type, record.Name, record.Model.Sections, sectionHardpoints, isBigShip);
    }

    private void CreateMesh(string recordType, string recordName, SectionRecord[] sections, HardpointRecord[][] sectionHardpoints, bool isBigShip)
    {
        Debug.Log($"Loading {recordType} {recordName}");

        var coordinateConverter = isBigShip ? _bigCoordinateConverter : _smallCoordinateConverter;

        MeshCreator meshCreater = new MeshCreator(coordinateConverter, _baseShip, _baseSection, _baseHardpoint, null, _paletteMappers[recordType]);

        sections = FilterSections(recordType, recordName, sections);

        var disabledMarkingSectionIndices = _showSpecialMarkings ? new int[0] : GetDisabledMarkingSectionIndices(recordType, recordName);

        _shipContainer = meshCreater.CreateGameObject(sections, sectionHardpoints, _currentLod, _currentFlightGroupColorIndex, disabledMarkingSectionIndices);

        foreach (var meshRenderer in _shipContainer.GetComponentsInChildren<MeshRenderer>())
            if (meshRenderer.name.StartsWith("Section", StringComparison.OrdinalIgnoreCase))
                meshRenderer.SetPropertyBlock(_materialPropertyBlocks[recordType]);
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
