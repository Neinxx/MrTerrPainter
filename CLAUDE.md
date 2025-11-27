# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Mr Terrain Painter V1** is a Unity Editor tool for procedurally placing and painting vegetation, props, and landscape elements on Unity Terrain objects. It supports interactive brush-based painting and bulk generation with advanced distribution algorithms.

**Target Unity Version:** Unity 2021.3.18f1+

**Current Branch:** V4 (active development)

## Development Commands

### Testing
```bash
# Run unit tests from Unity Editor
# Window → General → Test Runner
# Tests located in: Tests/Editor/BuildersTests.cs, Tests/Editor/EdgeLineTests.cs
```

### No Custom Build Scripts
This project uses standard Unity Editor compilation. No special build commands are required.

## High-Level Architecture

### Layered Architecture

```
UI Layer (UITK)
  ↓
State & Controllers Layer
  ↓
Services Layer (Core Logic)
  ↓
Data Layer (ScriptableObjects & Runtime Data)
```

### Core Components

**1. Entry Points (`Editor/MTPEntryPoints.cs`)**
- Unified entry point for all tool access methods
- Keyboard shortcuts: Alt+M (open window), Alt+B (toggle brush)
- Context menu: Right-click Terrain → "Mr Terrain Painter"
- Asset activation: Double-click VegetationProfile

**2. Main Window (`Editor/MrTerrainPainterWindow.cs`)**
- Central UI hub using Unity's UIElements (UITK)
- Manages session lifecycle, tab switching, event subscriptions
- Three main tabs: Painting, Generate, Settings

**3. PainterSession (`Editor/State/PainterSession.cs`)**
- Central state hub decoupling UI from business logic
- Holds: Config, BrushSettings, NoiseSettings, FilterSettings, SelectedTerrains, AvailableProfiles, CurrentProfile, UIState
- **Critical:** Acts as single source of truth for all controllers and views
- Constructed via `SessionInitBuilder` fluent pattern

### Key Services

**BrushPainter (`Editor/Services/BrushPainter.cs`)**
- Interactive painting operations (Paint, PaintMixed, Erase)
- Real-time brush preview rendering
- Uses VegetationPool for instance management

**VegetationGenerator (`Editor/Services/VegetationGenerator.cs`)**
- Bulk terrain generation with advanced filtering
- **Entry point:** `GenerateOnTerrain()` for bulk operations
- **Unified logic:** `MatchTerrain()` single entry point for height/slope filtering
- **Parent resolution:** `ResolveTargetParent()` centralized prefab-to-parent mapping

**BrushEngine (`Editor/Services/BrushEngine.cs`)**
- Low-level spatial sampling algorithms
- Distribution types: PoissonDisk, Cluster, JitteredGrid, Uniform, Adaptive, Halton, EdgeLine
- Burst-compiled Jobs support for performance
- Object pooling for List<Vector2> and List<Vector3>

**SceneInteractionService (`Editor/Services/SceneInteractionService.cs`)**
- Mouse input handling and raycasting
- Brush preview rendering in Scene View
- Facade detection visualization
- Throttled updates (50ms interval)

**VegetationPool (`Editor/Services/VegetationPool.cs`)**
- Hierarchical object pooling by terrain/item/prefab
- Spatial indexing via cell-based grid (2m cells)
- Supports undo/redo by recycling instances
- **Key methods:** `Get()`, `QueryInRadius()`, `IndexRegister/Unregister()`

**FacadeDetectionService & GlobalTerrainScanner**
- Detect terrain cliff faces for EdgeLine/FacadeStone placement
- Contour following, path smoothing, RDP simplification

### Controllers (MVC Pattern)

Located in `Editor/Controllers/`:
- **TerrainController:** Scene terrain scanning, selection
- **PaintingController:** Orchestrate brush painting
- **ProfileController:** VegetationProfile CRUD
- **PrefabPickerController:** Prefab selection UI
- **RefreshController:** UI refresh batching
- **PrefabAssignmentController:** Prefab type mapping

**Pattern:** Pure business logic, no UI knowledge, dependency-injected into PainterSession.

### Data Models

**VegetationProfile (`Runtime/Profiles/VegetationProfile.cs`)**
- ScriptableObject holding vegetation config
- Max 9 items per profile
- Fields: randomSeed, baseDensity, minSpacing, items[]

**VegetationItem (`Runtime/Profiles/VegetationItem.cs`)**
- Single prefab configuration
- Polymorphic via SerializeReference for IPlacementSettings
- Supports different placement strategies per PrefabType

**BrushSettings (Transient)**
- Real-time brush parameters: shape, size, strength, hardness
- Distribution type, cluster settings, falloff curve

**PrefabType (Enum)**
- Plant, VFX, Prop, Rock, Building, Landscape, FacadeStone
- Drives different placement and detection strategies

**VegetationInstance (`Runtime/Core/VegetationInstance.cs`)**
- MonoBehaviour component on placed instances
- Tracks: sourceTerrain, profileItemIndex, instanceId
- Used for identification and cleanup

### Configuration System

**MrTerrainPainterConfig (`Editor/Config/MrTerrainPainterConfig.cs`)**
- ScriptableObject for persistent settings
- Stores: Brush defaults, mapping entries (Type → Transform), facade parameters, undo settings, logging config
- **ConfigTools:** Static helper for asset creation, loading, updates

## Architectural Patterns

### Builder Pattern
Used for flexible object construction:
```csharp
// PainterSession initialization
var opts = new PainterSession.SessionInitBuilder()
    .OnRefreshList(callback)
    .IsGenerateMode(() => mode)
    .FindNearestTerrain(terrainFinder)
    .Build();

// SceneInteractionService
var service = new SceneInteractionService(
    new SceneInteractionService.Builder()
        .TerrainController(ctrl)
        .Brush(brush)
        .Build()
);
```

### Strategy Pattern
Pluggable filter and placement strategies via interfaces:
- `IFilterStrategy`
- `IPlacementOverrideStrategy`

### Object Pool Pattern
- Hierarchical pooling in VegetationPool
- List reuse pools in BrushEngine
- Reduces GC pressure, enables undo/redo

### Observer Pattern
Event-driven updates:
- `WindowStateChanged`
- `ProfilesUpdated`
- `ConfigUpdated`

### Service Locator (Controlled)
**MTPBrushContext:** Static context holding current brush, profile, config (scoped to editor tools)

## Critical Architecture Decisions

### Separation of Paint vs Generate
Two distinct pipelines:
- **Paint:** Real-time interactive (BrushPainter + SceneInteractionService)
- **Generate:** Bulk operations (VegetationGenerator)

### Unified Terrain Matching
Single entry point: `VegetationGenerator.MatchTerrain()`
- Avoids duplicate height/slope filtering logic
- Used by both paint and generate pipelines

### Centralized Parent Resolution
Single mapping: PrefabType → Transform (stored in config)
- `VegetationGenerator.ResolveTargetParent()` is the sole resolver
- Supports empty mappings with throttled logging

### Decoupling UI from Logic
- Controllers contain business logic, zero UI code
- PainterSession acts as data model for UI binding
- Event-driven updates propagate state changes

## Coding Conventions

### Naming
- **Services:** Verb-noun (BrushPainter, VegetationGenerator)
- **Views:** `*View` suffix (BrushView, PropertyPanelView)
- **Controllers:** `*Controller` suffix (TerrainController, PaintingController)
- **Settings:** Descriptive noun (BrushSettings, NoiseSettings, ClusterSettings)

### Early Return Pattern
Guard clauses used consistently:
```csharp
public void Method(Terrain t, List<T> list)
{
    if (t == null) return;
    if (list == null) return;
    // ... implementation
}
```

### Builder/Fluent Patterns
Preferred over large constructors for complex configuration.

### Caching & Performance
- Profile lists cached (ProfilesDirty flag)
- UI control references cached to avoid repeated Q<T> queries
- List reuse pools in BrushEngine
- Spatial index in VegetationPool

### Throttling & Batching
- Preview updates: 50ms throttle (PreviewIntervalSeconds)
- UI refresh batching via UIUpdateBatch
- Missing mapping logs throttled (~3s default)

### Event Subscription Safety
- Guard patterns: `_subscribed` flag prevents double subscription
- Proper cleanup in OnDisable()
- Null checks before unsubscribing

### Prefab Type Handling
Different behaviors per type:
- **StandardPlacementSettings:** Height/slope matching, normal alignment
- **LandscapePlacementSettings:** Facade detection, edge-following, stacking

### Undo/Redo Support
- Object pooling enables instance recycling for undo
- `VegetationPool.ShowInHierarchyAll()` toggles visibility for batch ops
- Bulk optimization threshold (config.undoBulkThreshold)

## Important Files

### Core Entry & State
- `Editor/MTPEntryPoints.cs` - All tool entry points
- `Editor/MrTerrainPainterWindow.cs` - Main window & UI lifecycle
- `Editor/State/PainterSession.cs` - Central state hub

### Services
- `Editor/Services/BrushPainter.cs` - Interactive painting
- `Editor/Services/VegetationGenerator.cs` - Bulk generation
- `Editor/Services/BrushEngine.cs` - Sampling algorithms
- `Editor/Services/SceneInteractionService.cs` - Input & preview
- `Editor/Services/VegetationPool.cs` - Object pooling
- `Editor/Services/FacadeDetectionService.cs` - Cliff detection
- `Editor/Services/GlobalTerrainScanner.cs` - Terrain scanning

### Data Models
- `Runtime/Profiles/VegetationProfile.cs` - Profile ScriptableObject
- `Runtime/Profiles/VegetationItem.cs` - Item configuration
- `Runtime/Core/BrushSettings.cs` - Brush state
- `Runtime/Core/VegetationInstance.cs` - Placed instance marker

### Configuration
- `Editor/Config/MrTerrainPainterConfig.cs` - Persistent config
- `Editor/Config/ConfigTools.cs` - Config management helpers

## Recent Refactoring (V4 Branch)

Active optimization efforts documented in `.trae/documents/`:
- Remove unused methods
- Unify terrain matching logic
- Extract & reuse sampling pipeline
- Optimize UI event handling & caching
- Strengthen null reference & event cleanup

Recent commits focus on brush optimization, UI fixes, selection effects, and pipeline refactoring.

## Key Insights for Development

1. **Always check PainterSession first** - It's the central state hub
2. **Use builder patterns** for complex object construction
3. **Single entry points matter** - MatchTerrain(), ResolveTargetParent()
4. **Respect the paint vs generate separation** - Different pipelines, don't mix
5. **Object pooling is critical** - Always use VegetationPool for instance management
6. **Throttling prevents performance issues** - Preview updates, logging, UI refresh
7. **Event cleanup is mandatory** - Always unsubscribe in OnDisable()
8. **Burst Jobs for performance** - TerrainSampleJob uses Burst compilation
9. **UITK throughout** - No legacy ImGUI, all UI is UIElements
10. **SerializeReference for polymorphism** - VegetationItem uses this for IPlacementSettings
- 永远用中文回答
- 代码风格: 提前返回, 单一职责, 如果参数过多则使用建造者模式,代码嵌套不得超过3层