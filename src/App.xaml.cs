using CityEditor.Config;
using CityWeaver.Config;
using CityWeaver.CoordinateSystems;
using DotSpatial.Projections;
using NeoCore.Logging;
using NeoCore.Photogrammetry;
using NeoCore.Runtime;
using System.Configuration;
using System.Data;
using System.Windows;
using ProjectFileData = NeoCore.Photogrammetry.ProjectData.ProjectFileData;


namespace MakeCityObjectsTest;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

         AppPaths.AppName = "MakeCityObjectsTest";
        string outputFolder = AppPaths.GetAppPath();
        SIMMETALogUtil.IsConsoleLog = true;
        SIMMETALogUtil.Init(outputFolder);

        string cityWeaverAppData = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CityWeaver");
        string configPath = System.IO.Path.Combine(cityWeaverAppData, "CityWeaverConf.json");

        JsonConfigStore configStore = new JsonConfigStore(configPath,TraceUtil.Error);
        var settings = configStore.Get<AppSettings>("App");

        var cityEditorSettings = configStore.Get<CityEditorSettings>("CityEditor");

        RoadEditor.Config.HostSettings.Initialize(
            ()=>settings.HeightAdjustSensitivity,
            ()=>settings.BridgePierSpacing,
            ()=>settings.BridgePierDiameter,
            ()=>settings.BridgeDeckThickness,
            ()=> settings.BridgeCopingHeight);

        if(!System.IO.File.Exists(configPath))
        {
            configStore.Save();
        }

        //var projectFilePath = @"C:\ProgramData\CityWeaver\Project\Kokusai2\Kokusai2.cwproj";
        var projectFilePath = @"C:\ProgramData\CityWeaver\Project\Kokusai\Kokusai.cwproj";

        string json = System.IO.File.ReadAllText(projectFilePath);
        ProjectFileData projectFileData = System.Text.Json.JsonSerializer.Deserialize<ProjectFileData>(json) ?? new ProjectFileData();

        TraceUtil.Assert(projectFileData != null, "Failed to load project file data.");

        ProjectContext.Initialize(projectFilePath , projectFileData);

        PhotogrammetryContext.Initialize();

        CityEditor.CityEditorManager.Instance.Initialize
            (PhotogrammetryContext.Current!,
            PhotogrammetryContext.Calculator!);

        PhotogrammetryContext.Calculator!.SetConfig(projectFileData);

        if(System.IO.File.Exists(projectFileData.DEMPath))
        {
            CityEditor.CityEditorManager.Instance.MakeDem(projectFileData.DEMPath);
        }

        var mainWindow = new CityWeaver.Project.MainWindow();
        mainWindow.Show();
        mainWindow.SetProjectData(projectFilePath, projectFileData);
        
    }

    override protected void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        CityEditor.CityEditorManager.Instance.Close();
    }
}
