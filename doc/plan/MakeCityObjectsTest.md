# MakeCityObjectsTest 만들기 — 멀티/전체 선택으로 MakeCityObjects(OBJ) 호출

## Context (배경)

`ShpImportExportTest`(WPF, net8.0-windows)는 맵뷰에서 도로/건물/차선을 멀티선택하고
SHP를 입출력하는 테스트 앱이다. 이를 복제해 `MakeCityObjectsTest`를 만들고,
선택한(또는 전체) 도로·건물을 `CityEditor.CityEditorManager.Instance.MakeCityObjects(...)`
로 넘겨 OBJ 모델을 생성하는 것이 목표다.

`CityWeaver_main/src/CityWeaver/MainWindow.xaml.cs:143-171`(`PreviewSelectedPolygons`)에
이미 동일한 형태로 `Dictionary<string, List<CityObjTemplate>>`를 만들어
`ShowPreview`에 넘기는 정답 패턴이 있다. 우리는 이를 그대로 따르되 호출 대상만
`MakeCityObjects(..., ExportFormat.OBJ)`로 바꾼다.

**전체 선택 정책**: 별도 "전체 선택" 버튼/맵뷰 API를 만들지 않는다.
SHP 출력의 `hasSelected` 패턴과 동일하게 — 선택이 하나도 없으면 전체를 대상으로 한다.

### 재사용할 기존 자산 (신규 코드 최소화)
- `RoadEditor/src/RoadEditor/Templates/RoadObjTemplate.cs:58` `ToCityObjects()` → `List<CityObjTemplate>`
- `BuildingEditor/src/BuildingEditor/Templates/ObjTemplate.cs:46` `ToCityObject()` → `CityObjTemplate`
- `CityEditor/src/CityEditorManager.cs:976` `MakeCityObjects(Dictionary<string,List<CityObjTemplate>>, outputPath, out ignorList, ExportFormat)`
- 선택 상태 수집 로직 `MainWindow.xaml.cs:295` `UpdateSelectedGeometryList` (그대로 유지)
- 그룹 키 규칙: 건물 `ObjectGroupId ?? "B0"`, 도로 `ObjectGroupId ?? "R0"`, 도로는 Flat/Bridge만

## 가정 (Assumptions)
- 신규 프로젝트는 `ShpImportExportTest`를 통째로 복제 후 이름만 바꾼다.
  대상 위치 `d:\work\MakeCityObjectsTest`는 `d:\work\ShpImportExportTest`와 형제 폴더라
  `.sln`/`.csproj`의 상대참조(`..\CityWeaver`, `..\NeoCore` 등)는 수정 없이 그대로 해석된다.
- 프로젝트 정체성만 리네임: 어셈블리/앱 이름, `App` 네임스페이스, `AppPaths.AppName`.
  `MainWindow`는 기존과 동일하게 `CityWeaver.Project` 네임스페이스 유지(불필요한 변경 회피).
- SHP 입력/출력 버튼과 기능은 그대로 둔다(데이터를 store에 적재 → OBJ 출력의 입력원).
- 프로젝트 데이터는 기존 App과 동일하게 `Kokusai.cwproj` 하드코딩 경로를 그대로 사용.
- 차선(Lane)은 이번 OBJ 출력 대상에서 제외(건물/도로만). CityObjTemplate 변환 경로가 도로/건물만 존재.

## 변경 파일 및 작업

### 1) 프로젝트 스캐폴딩 (복제 + 리네임)  — [적용 완료]
`d:\work\ShpImportExportTest`의 `src/` 전체와 `ShpImportExportTest.sln`을
`d:\work\MakeCityObjectsTest`로 복사(단, `bin/`,`obj/`,`.vs/` 제외).

리네임 항목:
- `ShpImportExportTest.sln` → `MakeCityObjectsTest.sln`, 내부 메인 프로젝트 항목
  `"ShpImportExportTest", "src\ShpImportExportTest.csproj"` → `"MakeCityObjectsTest", "src\MakeCityObjectsTest.csproj"`
  (나머지 NeoCore/NeoAICore/MapPrimeNet/laszip.net/CityWeaver 항목은 그대로)
- `src/ShpImportExportTest.csproj` → `src/MakeCityObjectsTest.csproj`
  (`src/ShpImportExportTest.csproj.user`도 동일 리네임 또는 삭제)
- `App.xaml` `x:Class="ShpImportExportTest.App"` / `xmlns:local` → `MakeCityObjectsTest`
- `App.xaml.cs` `namespace ShpImportExportTest;` → `namespace MakeCityObjectsTest;`
- `App.xaml.cs` `AppPaths.AppName = "ShpImportExportTest";` → `"MakeCityObjectsTest"`
- `MainWindow.xaml`의 타이틀 텍스트 `"ShpImportTest"` → `"MakeCityObjectsTest"`(표시용, 선택)

> **참고(검증 결과)**: `src/IO/`(로컬 SHP importer/exporter 6개)는 복사하지 않아도 된다.
> 동일 클래스가 CityWeaver 본체 `CityWeaver.IO`(public)에 존재하며
> (`d:\work\CityWeaver\src\CityWeaver\IO\`), `using CityWeaver.IO;`로 해석되어 빌드된다.
> (원본은 로컬 복사본이 참조 어셈블리보다 우선했던 것 → 로컬 IO 제거는 중복 제거)
> `CityEditor`는 `CityWeaver.csproj`가 이미 프로젝트 참조하여 전이적으로 빌드된다.

### 2) App.xaml.cs — CityEditorManager 초기화 추가
OBJ 메시 생성 시 지형/DEM·측량 컨텍스트가 필요하다. `CityWeaver_main/App.xaml.cs:235-250`을
그대로 미러링한다. `PhotogrammetryContext.Initialize()` 직후에 삽입.

**Before** (`src/App.xaml.cs`, 62-63 부근)
```csharp
PhotogrammetryContext.Initialize();
PhotogrammetryContext.Calculator!.SetConfig(projectFileData);
```
**After**
```csharp
PhotogrammetryContext.Initialize();

CityEditor.CityEditorManager.Instance.Initialize(
    PhotogrammetryContext.Current!, PhotogrammetryContext.Calculator!);

PhotogrammetryContext.Calculator!.SetConfig(projectFileData);

if (System.IO.File.Exists(projectFileData.DEMPath))
    CityEditor.CityEditorManager.Instance.MakeDem(projectFileData.DEMPath);
```

### 3) MainWindow.xaml.cs — OBJ 출력 배선
`ObjOutputButton_Click`(현재 566행, 비어 있음)에 구현하고, 헬퍼 `BuildCityObjects()` 추가.
`_roads`/`_buildings`(선택 시)와 store 전체(무선택 시)를 모두 처리.

**Before** (`src/MainWindow.xaml.cs:566`)
```csharp
private void ObjOutputButton_Click(object sender, RoutedEventArgs e)
{

}
```
**After**
```csharp
private void ObjOutputButton_Click(object sender, RoutedEventArgs e)
{
    var cityObjList = BuildCityObjects();
    if (cityObjList.Count == 0)
    {
        MessageBox.Show(this, "출력할 데이터가 없습니다.", "OBJ 출력",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select OBJ Output folder" };
    if (dialog.ShowDialog() != true)
        return;

    try
    {
        CityEditor.CityEditorManager.Instance.MakeCityObjects(
            cityObjList, dialog.FolderName, out var ignorList, CityEditor.ExportFormat.OBJ);

        cityWeaverMapViewSecond.ClearSelection();

        MessageBox.Show(this,
            $"OBJ 출력 완료\n- 그룹 {cityObjList.Count}개 → {dialog.FolderName}\n- 스킵(기존파일) {ignorList.Count}개",
            "OBJ 출력", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show(this, $"OBJ 출력 실패: {ex.Message}", "OBJ 출력",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

// 선택이 있으면 선택분, 없으면 store 전체를 CityObjTemplate 그룹으로 변환한다.
private Dictionary<string, List<CityEditor.Templates.CityObjTemplate>> BuildCityObjects()
{
    var cityObjList = new Dictionary<string, List<CityEditor.Templates.CityObjTemplate>>();

    void AddTo(string key, IEnumerable<CityEditor.Templates.CityObjTemplate> objs)
    {
        if (!cityObjList.TryGetValue(key, out var list))
            cityObjList[key] = list = new List<CityEditor.Templates.CityObjTemplate>();
        list.AddRange(objs);
    }

    bool hasSelected = _selectedBuildingIds.Count > 0 || _selectedRoadIds.Count > 0;

    if (hasSelected)
    {
        foreach (var (key, obj) in _buildings)
            AddTo(key, new[] { obj.ToCityObject() });

        foreach (var (key, obj) in _roads)
            AddTo(key, obj.ToCityObjects());
    }
    else
    {
        foreach (var bt in _buildingTemplateStore.Templates)
        {
            string bKey = string.IsNullOrEmpty(bt.ObjectGroupId) ? "B0" : bt.ObjectGroupId;
            AddTo(bKey, new[] { new BuildingEditor.Templates.ObjTemplate(bt).ToCityObject() });
        }

        foreach (var rt in _roadTemplateStore.Templates)
        {
            if (rt.Type is not BaseRoadTemplate.RoadType.Flat and not BaseRoadTemplate.RoadType.Bridge)
                continue;

            string rKey = string.IsNullOrEmpty(rt.ObjectGroupId) ? "R0" : rt.ObjectGroupId;
            AddTo(rKey, new RoadEditor.Templates.RoadObjTemplate(rt).ToCityObjects());
        }
    }

    return cityObjList;
}
```

> 참고: 위 store 순회는 `UpdateSelectedGeometryList`(295행)의 키 규칙·타입 필터와 동일.

## 검증 (Verification)
1. `d:\work\MakeCityObjectsTest`에서 `dotnet build MakeCityObjectsTest.sln` (또는 VS 빌드) 성공 확인.
2. 실행 → `Kokusai.cwproj` 로드, 맵에 도로/건물 렌더 확인.
3. **선택 없이** "OBJ 출력" → 폴더 선택 → store 전체가 그룹별 `.obj`로 생성되는지,
   메시지의 그룹 수가 store 개수 기반과 맞는지 확인.
4. 맵에서 일부 도로/건물 **박스/개별 멀티선택** 후 "OBJ 출력" → 선택분만 생성되는지 확인.
5. 재실행 후 같은 폴더로 다시 출력 → 기존 파일은 `ignorList`로 스킵되어 "스킵 N개"로 보고되는지 확인.
6. 필요 시 SHP 입력으로 데이터를 적재한 뒤 3~5 반복.

## 리스크 / 열린 항목
- `MakeCityObjects` 내부 메시 생성이 DEM/측량 컨텍스트에 의존할 수 있음 → App 초기화(2번)로 대응.
  `Kokusai` 프로젝트에 `DEMPath`가 없으면 `MakeDem` 스킵되며, 지형 없는 모델로 생성될 수 있음(정상).
- `MakeCityObjects`는 UI 대기창(`CityEditorCustomUtil.waitingFormShow/Close`)을 띄운다 — WPF 스레드에서 정상 동작 확인 필요.
- Lane OBJ 출력은 범위 외(추후 필요 시 CityObjTemplate 변환 경로 추가 검토).

## 진행 현황
- [x] 1) 프로젝트 스캐폴딩(복제 + 리네임)
- [ ] 2) App.xaml.cs — CityEditorManager 초기화 추가
- [ ] 3) MainWindow.xaml.cs — OBJ 출력 배선
