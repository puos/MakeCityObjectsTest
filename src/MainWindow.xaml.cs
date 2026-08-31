using A4L.MapprimeNet.Geometries;
using A4L.MapprimeNet.MapView.Renderers;
using CityWeaver.IO;
using CityWeaver.MapView;
using CityWeaver.MapView.Layers;
using CityWeaver.Templates;
using CityWeaver.Work;
using LaneFacilityEditor.Layer;
using LaneFacilityEditor.Work;
using NeoCore.Logging;
using RoadEditor.Templates;
using RoadEditor.Work;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ProjectFileData = NeoCore.Photogrammetry.ProjectData.ProjectFileData;
using Geometry = NetTopologySuite.Geometries.Geometry;
using NeoCore.Runtime;
using System.Diagnostics;

namespace CityWeaver.Project;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private string? _projectFilePath;
    private ProjectFileData? _projectFileData;
    private readonly RoadTemplateStore _roadTemplateStore = new();
    private readonly BuildingTemplateStore _buildingTemplateStore = new();
    private readonly LaneFeatureStore _laneStore = new();
    
    private readonly List<(string, RoadEditor.Templates.RoadObjTemplate)> _roads = new();
    private readonly List<(string, BuildingEditor.Templates.ObjTemplate)> _buildings = new();
    
    private readonly HashSet<Guid> _selectedRoadIds = new();
    private readonly HashSet<Guid> _selectedBuildingIds = new();
    private readonly HashSet<Guid> _selectedLaneIds = new();

    private bool _isProjectDataLoaded;
    private bool _initialZoomRequested;
    private bool _isContentRendered;

    private int _sourceEpsg = 5186;

    public MainWindow()
    {
        InitializeComponent();

        cityWeaverMapViewSecond.InitializeRegistrationView(1);
        cityWeaverMapViewSecond.BindStores(_roadTemplateStore, _buildingTemplateStore,_laneStore);

        cityWeaverMapViewSecond.FeatureSelected -= RegistrationView_FeatureSelected;
        cityWeaverMapViewSecond.FeatureSelected += RegistrationView_FeatureSelected;

        cityWeaverMapViewSecond.BoxSelected -= RegistrationView_BoxSelect;
        cityWeaverMapViewSecond.BoxSelected += RegistrationView_BoxSelect;

    }

    private void Window_Closed(object sender, EventArgs e)
    {
        Close();
    }

    private void DockPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
         WindowState = WindowState.Minimized;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }



    private void CityWeaverMapView_Loaded(object sender, RoutedEventArgs e)
    {

    }

    public void SetProjectData(string projectFilePath,ProjectFileData projectFileData)
    {
        if (projectFileData == null)
            return;

        _sourceEpsg = projectFileData.EpsgCode;
        _projectFilePath = projectFilePath;
        _projectFileData = projectFileData;

        LoadRoads();
        LoadBuildings();
        LoadLanePolygons();


        cityWeaverMapViewSecond.RenderRoads();
        cityWeaverMapViewSecond.RenderBuildings();
        cityWeaverMapViewSecond.RenderLanePolygons();

        _isProjectDataLoaded = true;
        _initialZoomRequested = false;
        TryRequestInitialZoomFit();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        _isContentRendered = true;
        TryRequestInitialZoomFit();
    }

    private void TryRequestInitialZoomFit()
    {
        if (!_isProjectDataLoaded || !_isContentRendered || _initialZoomRequested)
            return;

        _initialZoomRequested = true;
        ZoomFitAfterFirstDraw(cityWeaverMapViewSecond);
    }
    private static void ZoomFitAfterFirstDraw(CityWeaverMapView view)
    {
        Action<int, Painter, ExRectangle, double>? handler = null;
        handler = (_, __, ___, ____) =>
        {
            view.OnAfterDraw -= handler;
            view.ZoomFit();
            view.Redraw();
        };

        view.OnAfterDraw += handler;
        view.Redraw();
    }

    private void LoadRoads()
    {
        if (string.IsNullOrEmpty(_projectFilePath))
            return;

        string path = RoadJsonStorage.GetFilePath(_projectFilePath);
        if (!System.IO.File.Exists(path))
        {
            _roadTemplateStore.Clear();
            return;
        }

        try
        {
            List<BaseRoadTemplate> templates = RoadJsonStorage.Load(path, out int skippedRoadCount);
            string? backupPath = skippedRoadCount > 0
                ? RoadJsonStorage.CreateInvalidBackup(path)
                : null;

            _roadTemplateStore.Clear();
            foreach (var template in templates)
            {
                _roadTemplateStore.Add(template);
            }
            if (backupPath != null)
            {
                TraceUtil.WriteLine($"Some roads could not be loaded. The original file was backed up as {System.IO.Path.GetFileName(backupPath)}.");
            }
        }
        catch (Exception ex)
        {
        }
    }

    private void LoadBuildings() 
    {
        if(string.IsNullOrEmpty(_projectFilePath))
            return;

        string path = BuildingJsonStorage.GetFilePath(_projectFilePath);
        if (!System.IO.File.Exists(path))
        {
            _buildingTemplateStore.Clear();
            return;
        }

        try
        {
            var templates = BuildingJsonStorage.Load(path, out int skippedBuildingCount);
            string? backupPath = skippedBuildingCount > 0
                ? BuildingJsonStorage.CreateInvalidBackup(path)
                : null;

            _buildingTemplateStore.Clear();
            foreach (var template in templates)
            {
                _buildingTemplateStore.Add(template);
            }
            if(backupPath != null)
            {
                TraceUtil.WriteLine($"Some buildings could not be loaded. The original file was backed up as {System.IO.Path.GetFileName(backupPath)}.");
            }
        }
        catch (Exception ex)
        {
        }
    }

    private void LoadLanePolygons()
    {
         _laneStore.Clear();

        if (string.IsNullOrWhiteSpace(_projectFilePath))
            return;

        foreach (string layerName in LanePolygonLayer.RegistrationLayerNames)
        {
            string path = LaneJsonStorage.GetFilePath(_projectFilePath, layerName);

            if (!System.IO.File.Exists(path))
            {
                continue;
            }

            try
            {
                LaneJsonStorage.LaneJsonStyle? layerStyle = null;

                List<SHPLayer.FeatureItem> features = LaneJsonStorage.Load(path, out int skippedFeatureCount, out _);

                _laneStore.SetLayer(layerName, features);
                cityWeaverMapViewSecond.ApplyLaneLayerStyle(layerName, layerStyle);

                if (skippedFeatureCount > 0)
                {
                    string? backupPath = LaneJsonStorage.CreateInvalidBackup(path);
                    
                    if (backupPath != null)
                    {
                        TraceUtil.WriteLine($"Some lane polygons in layer {layerName} could not be loaded. The original file was backed up as {System.IO.Path.GetFileName(backupPath)}.");
                    }
                }
            }
            catch (Exception ex)
            {
                TraceUtil.WriteLine($"Failed to load lane polygons for layer {layerName}: {ex.Message}");
            }
        }
    }

    private void RegistrationView_BoxSelect(object? sender,CityWeaverMapView.BoxSelectedEventArgs e)
    {
        UpdateSelectedGeometryList(e.Features);
    }

    private void RegistrationView_FeatureSelected(object? sender, CityWeaverMapView.FeatureSelectedEventArgs e)
    {
        if(e.Additive)
        {
            UpdateSelectedGeometryList(e.Features);
            return;
        }


        if (e.ElementId is Guid id &&
                 cityWeaverMapViewSecond.Layers.Find(layer => layer.Name == e.LayerName) is { } layer)
        {
            UpdateSelectedGeometryList(new[] { new FeatureRef(layer, id) });
            return;
        }

        UpdateSelectedGeometryList(Array.Empty<FeatureRef>());
    }

    private void UpdateSelectedGeometryList(IEnumerable<FeatureRef> features) 
    {
        _roads.Clear();
        _buildings.Clear();
        _selectedRoadIds.Clear();
        _selectedBuildingIds.Clear();
        _selectedLaneIds.Clear();

        foreach (FeatureRef f in features)
        {
            switch(f.Layer)
            {
                case LanePolygonLayer:
                     _selectedLaneIds.Add(f.Id);
                    break;
                case RoadLayer when _roadTemplateStore.TryGet(f.Id,out var rt) && rt != null:
                    {
                        _selectedRoadIds.Add(f.Id);

                        if (rt.Type is not BaseRoadTemplate.RoadType.Flat and not BaseRoadTemplate.RoadType.Bridge)
                        {
                            continue;
                        }

                        var objectGroupId = rt.ObjectGroupId;
                        string rKey = string.IsNullOrEmpty(rt.ObjectGroupId) ? "R0" : rt.ObjectGroupId;

                        if (rt.Type is not BaseRoadTemplate.RoadType.Flat and not BaseRoadTemplate.RoadType.Bridge)
                        {
                            continue;
                        }

                        _roads.Add((rKey, new RoadObjTemplate(rt)));
                    }
                    break;
                case BuildingLayer when _buildingTemplateStore.TryGet(f.Id,out var bt) && bt != null:
                    {
                        var objectGroupId = bt.ObjectGroupId;
                        string bKey = string.IsNullOrEmpty(bt.ObjectGroupId) ? "B0" : bt.ObjectGroupId;
                        var objTemplate = new BuildingEditor.Templates.ObjTemplate(bt);
                        _buildings.Add((bKey, objTemplate));
                        _selectedBuildingIds.Add(f.Id);
                    }
                    break;
            }
        }

        TraceUtil.WriteLine($"[정합창] 영역 선택: 도로 {_roads.Count} , 건물 {_buildings.Count}");
    }

    private void GmlOutputButton_Click(object sender, RoutedEventArgs e)
    {

    }

    private void ObjOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var cityObjList = BuildCityObjects();
        if (cityObjList.Count == 0)
        {
            MessageBox.Show("No city objects to export.");
            return;
        }
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select OBJ Output folder" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var timeCheck = Stopwatch.StartNew();

            CityEditor.CityEditorManager.Instance.MakeCityObjects(
             cityObjList, dialog.FolderName, out var ignorList, CityEditor.ExportFormat.OBJ);

            cityWeaverMapViewSecond.ClearSelection();

            timeCheck.Stop();

            MessageBox.Show(this,
            $"OBJ output  complete\n- group {cityObjList.Count} → {dialog.FolderName}\n- skipped (existing files) {ignorList.Count}\n- elapsed time {timeCheck.ElapsedMilliseconds * 0.001}",
            "OBJ output", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export OBJ: {ex.Message}");
        }
    }

    private void OpenUsdOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var cityObjList = BuildCityObjects();
        if(cityObjList.Count == 0)
        {
            MessageBox.Show("No city objects to export.");
            return;
        }
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select USD Output folder" };
        if (dialog.ShowDialog() != true)
            return;

        try 
        {
            var timeCheck = Stopwatch.StartNew();

            CityEditor.CityEditorManager.Instance.MakeCityObjects(
             cityObjList, dialog.FolderName, out var ignorList, CityEditor.ExportFormat.USD);

            cityWeaverMapViewSecond.ClearSelection();

            timeCheck.Stop();

            MessageBox.Show(this,
            $"USD output  complete\n- group {cityObjList.Count} → {dialog.FolderName}\n- skipped (existing files) {ignorList.Count} \n- elapsed time { timeCheck.ElapsedMilliseconds * 0.001}",
            "USD output", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export USD: {ex.Message}");
        }
    }

    private Dictionary<string, List<CityEditor.Templates.CityObjTemplate>> BuildCityObjects() 
    {
        var cityObjList = new Dictionary<string, List<CityEditor.Templates.CityObjTemplate>>();

        void AddTo(string key,IEnumerable<CityEditor.Templates.CityObjTemplate> objs)
        {
            if(!cityObjList.TryGetValue(key,out var list))
                cityObjList[key] = list = new List<CityEditor.Templates.CityObjTemplate>();
            list.AddRange(objs);
        }

        bool hasSelected = _selectedRoadIds.Count > 0 || _selectedBuildingIds.Count > 0;

        if (hasSelected)
        {
            foreach (var (key, obj) in _buildings)
                AddTo(key, new[] { obj.ToCityObject() });

            foreach(var (key,obj) in _roads)
                AddTo(key, obj.ToCityObjects());
        }
        else
        {
            foreach(var bt in _buildingTemplateStore.Templates)
            {
                string bKey = string.IsNullOrEmpty(bt.ObjectGroupId) ? "B0" : bt.ObjectGroupId;
                AddTo(bKey, new[] { new BuildingEditor.Templates.ObjTemplate(bt).ToCityObject() });
            }

            foreach(var rt in _roadTemplateStore.Templates)
            {
                if(rt.Type is not BaseRoadTemplate.RoadType.Flat and not BaseRoadTemplate.RoadType.Bridge)
                    continue;

                string rKey = string.IsNullOrEmpty(rt.ObjectGroupId) ? "R0" : rt.ObjectGroupId;
                AddTo(rKey, new RoadObjTemplate(rt).ToCityObjects());
            }
        }

        return cityObjList;
    }
}