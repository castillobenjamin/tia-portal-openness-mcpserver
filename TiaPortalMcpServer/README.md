# TIA Portal Openness MCP Server

An MCP (Model Context Protocol) server that exposes TIA Portal Openness API functionality, allowing LLMs to programmatically interact with Siemens TIA Portal for PLC programming and automation engineering.

## Overview

This server provides tools for:
- Project management (create, open, save, close projects)
- Hardware configuration (add devices, configure parameters)
- Software/PLC development (add blocks, tags, compilation)
- Utility operations (project info, library listing)

## Prerequisites

- **TIA Portal V20** installed and licensed on the host machine
- **.NET Framework 4.8**
- The server runs on stdio transport for MCP communication
- Default network endpoint (if a network transport is enabled): `127.0.0.1:3000` — override with environment variables `MCP_HOST` / `MCP_PORT` or configuration keys `MCP:Host` / `MCP:Port`

### CI Build Requirement

The server project cannot be built from nuget.org packages alone. The Siemens TIA Openness NuGet package only wires up references; it still expects `Siemens.Engineering.dll` to be available through `TiaPortalLocation` or a matching TIA Portal registry entry.

The release workflow uses `windows-latest` and stages a private zip bundle from a GitHub Release asset containing the TIA Portal V20 `PublicAPI\V20` assemblies.

Configure these repository variables:

- `TIA_PUBLICAPI_RELEASE_REPOSITORY`: artifact repository in `owner/repo` format
- `TIA_PUBLICAPI_RELEASE_TAG`: release tag that contains the zip asset
- `TIA_PUBLICAPI_RELEASE_ASSET_NAME`: zip asset name, for example `PortalV20-PublicAPI-V20.zip`

Configure this repository secret:

- `TIA_PUBLICAPI_GITHUB_TOKEN`: token with read access to the artifact repository release assets

The bundle must contain `Siemens.Engineering.dll` somewhere in the archive. Two layouts are accepted:

1. **Preferred** – DLL already nested under a `PublicAPI\V20` directory tree:
   ```text
   <some-root>\PublicAPI\V20\Siemens.Engineering.dll
   ```
   The workflow derives `TiaPortalLocation` from `<some-root>` and passes it to MSBuild automatically.

2. **Flat** – DLL (and sibling assemblies) at the archive root or any other location *not* containing `PublicAPI\V20` in the path:
   ```text
   Siemens.Engineering.dll
   Siemens.Engineering.Hmi.dll
   ...
   ```
   The workflow automatically constructs the expected `PublicAPI\V20` directory tree from the DLL's source directory so that MSBuild can resolve `TiaPortalLocation`.

## Installation

1. Ensure TIA Portal V20 is installed at the default location
2. Build the project:
   ```bash
   dotnet build
   ```
3. Run the server:
   ```bash
   dotnet run
   ```

### CLI-Based Installation

The repository also contains a TypeScript CLI under `../cli` for release-based installation and updates.

Examples:

```bash
npx @chewcw/tia-portal-openness-mcpserver list
npx @chewcw/tia-portal-openness-mcpserver install --server-version v1.0.0
npx @chewcw/tia-portal-openness-mcpserver update
```

## Tools Available

### Project Management
- `projects_create` - Create a new TIA Portal project (prompts for name/path if missing and client supports elicitation)
- `projects_open` - Open an existing project (prompts for path if missing and client supports elicitation)
- `projects_close` - Close the current project
- `projects_save` - Save the current project
- `projects_save_as` - Save the current project to a new, empty directory and continue with the copy

### Device
- `devices_list` - List all devices in the project
- `devices_create` - Add a new device to the project
- `devices_delete` - Remove a device from the project
- `devices_get_attributes` - Get attributes of a device
- `devices_set_attribute` - Set an attribute on a device
- `devices_get_app_id` - Get the application ID of a device
- `devices_set_app_id` - Set the application ID of a device
- `devices_search_catalog` - Search the hardware catalog for devices

### Device Items
- `deviceitems_list` - List device items in a device
- `deviceitems_get_attributes` - Get attributes of a device item
- `deviceitems_plug_move` - Move a device item to a new position
- `deviceitems_copy` - Copy a device item to a new position
- `deviceitems_delete` - Delete a device item from a device
- `catalog_search_device_items` - Search the hardware catalog for device items

### Software/PLC Development

The block, tag and software tools below (and the advanced block tools) take an optional `unitName`. Leave it empty to work on the device's main PLC software, or pass a name from `software_units_list` to work inside that Software Unit.

- `software_add_block` - Add a block to a PLC
- `blocks_list` - List all blocks in a PLC
- `software_get_block_hierarchy` - Get the call hierarchy of a block
- `software_units_list` - List the Software Units (standard and safety) of a PLC
- `software_paths_list` - List every PLC software and Software Unit in the project (including devices in device folders) as `Device` / `Device/Unit` paths, with the `deviceName` and `unitName` to pass to the other tools
- `software_tree_get` - Get the project tree of a PLC or Software Unit: program blocks, PLC data types, tag tables and external sources as nested groups with group paths and object counts (`includeItems=false` for groups only)
- `tags_create` - Create PLC tags
- `tags_list` - List PLC tags
- `tags_tagtable_create` - Create tag tables
- `tags_tagtable_list` - List tag tables
- `tags_tagtable_get` - Get a tag table
- `tags_tagtable_delete` - Delete a tag table
- `tags_tagtable_open_editor` - Open a tag table in the TIA editor
- `tags_group_create` - Create a tag group
- `tags_group_list` - List tag groups
- `tags_group_find` - Find a tag group
- `tags_group_delete` - Delete a tag group
- `tags_group_system_get` - Get system tag groups
- `tags_constants_user_list` - List user constants
- `tags_constants_system_list` - List system constants

### Blocks (Advanced)
- `blocks_external_source_add` - Add an external source
- `blocks_external_source_list` - List external sources
- `blocks_external_source_generate_all` - Generate all external sources
- `blocks_external_source_generate_with_options` - Generate external source with options
- `blocks_external_source_generate_in_group` - Generate external source in a group
- `blocks_external_source_delete` - Delete an external source
- `blocks_source_generate` - Generate source from blocks
- `blocks_source_generate_from_block` - Generate source from a single block
- `blocks_prodiag_assigned_get` - Get ProDiag assignment for a member
- `blocks_prodiag_assigned_set` - Set ProDiag assignment for a member
- `blocks_prodiag_attributes_get` - Get ProDiag attributes
- `blocks_prodiag_export_csv` - Export ProDiag to CSV
- `blocks_udt_delete` - Delete a UDT
- `blocks_system_types_list` - List system types
- `blocks_user_types_list` - List user types
- `blocks_editor_open_block` - Open a block in the TIA editor
- `blocks_editor_open_type` - Open a type in the TIA editor
- `blocks_fingerprints_get` - Get block fingerprints
- `blocks_ob_priority_set` - Set OB priority

### SimaticML XML Exchange
Export and import blocks, PLC data types (UDTs) and PLC tag tables as SimaticML `.xml` files. These tools also take an optional `unitName`. Exports search all groups. `importOption` (`None`, `Override`, `SkipInactiveCultures`, `ActivateInactiveCultures`) and `swImportOption` (`IgnoreStructuralChanges`, `IgnoreMissingReferencedObjects`, `IgnoreUnitAttributes`) take comma-separated names. Imports support `dryRun`, skip files that hold the other kind of object, retry files whose dependencies were not imported yet, and save the project after a successful block or type import.

All SimaticML exports leave out the document info header (export settings, installed products, creation timestamp), so exporting an unchanged object again gives an identical file. `includeDefaults` (default `false`) also writes attributes that still have their default value; leave it off to match files exported without it.

- `blocks_export_as_xml` - Export one block as SimaticML
- `blocks_export_all_as_xml` - Export all blocks, optionally filtered by name/regex
- `blocks_import_from_xml` - Import the blocks of one SimaticML file into a block group
- `blocks_import_all_from_xml` - Import the blocks of all SimaticML files in a directory
- `types_export_as_xml` - Export one PLC data type as SimaticML
- `types_export_all_as_xml` - Export all PLC data types, optionally filtered by name/regex
- `types_import_from_xml` - Import the PLC data types of one SimaticML file into a type group
- `types_import_all_from_xml` - Import the PLC data types of all SimaticML files in a directory
- `tags_tagtable_export` - Export one PLC tag table as SimaticML, to a directory or an `.xml` file path
- `tags_tagtable_export_all` - Export all PLC tag tables, optionally filtered by name/regex
- `tags_tagtable_import` - Import one tag table file into a tag table group
- `tags_tagtable_import_all` - Import the tag tables of all SimaticML files in a directory

Tag table exports fail when TIA Portal writes no file or an empty one. Tag table imports take `importOption` only (default `Override`) and do not save the project; each imported file's result says so. Call `projects_save` to keep the result.

### SIMATIC SD Documents
Export and import blocks as SIMATIC SD documents (`.s7dcl`/`.s7res`). These tools also take an optional `unitName`.

- `blocks_export_as_documents` - Export one block as SD documents
- `blocks_export_all_as_documents` - Export all blocks, optionally filtered by name/regex
- `blocks_import_from_documents` - Import one document pair into a block group
- `blocks_import_all_from_documents` - Import all document pairs in a directory

### Keeping the TIA Folder Structure (`preservePath`)
The bulk export and import tools for SimaticML and SD documents, and the single-object exports, take `preservePath`. With `preservePath=true`, exports mirror the TIA project tree below `exportPath`: blocks go below `Program blocks/`, PLC data types below `PLC data types/` and tag tables below `PLC tags/`, each followed by the group path. All three can share one `exportPath`:

```text
exportPath/
  Program blocks/
    Main.xml
    Motion/Axis/Axis_FB.xml
  PLC data types/
    Motion/Axis_UDT.xml
  PLC tags/
    Default tag table.xml
    IO/Inputs.xml
```

Bulk imports with `preservePath=true` read the same layout back. Pass the same path as `importPath`: the tree starts in `importPath/Program blocks` (blocks) or `importPath/PLC data types` (types) or `importPath/PLC tags` (tag tables) when that folder exists, and in `importPath` itself otherwise. Each subdirectory is imported into the group of the same path below `groupName`, and missing groups are created. Without `preservePath`, exports write all files directly into `exportPath` and imports read only the files directly in `importPath`.

### Hardware Configuration (CAx)
Export the hardware configuration as AutomationML for review and version control. There is no CAx import tool: a CAx import creates objects and does not update an existing project.

- `cax_export` - Export the whole project, or one device (`deviceName`), as an `.aml` file in an existing `exportPath`

TIA Portal's transfer messages go to a `.log` file of the same name next to the `.aml`. The export can succeed partially, so the response gives the overall state, error and warning counts and the error and warning messages (up to 100 each; `messagesTruncated` says when the log holds more). An existing `.aml` is replaced only with `overwrite=true`. With `normalize=true`, element IDs are replaced with GUIDs derived from each element's path (element name, `Name` and position among equally named siblings), references to them are rewritten to match, write timestamps (`LastWritingDateTime`) are set to `1970-01-01T00:00:00`, and the log leaves out timestamps, so exporting an unchanged project again gives identical files. Nothing else is changed or reordered. The AML does not hold every parameter shown in the TIA Portal properties.

### Utility Operations
- `utilities_get_project_info` - Get project information
- `utilities_list_libraries` - List available libraries
- `utilities_elicit_user_input` - Ask the user for missing information via MCP elicitation

### Compilation
The compilation tools return the overall state, error and warning counts, and each compiler message with the path of the object it belongs to (for example `MSPLC_M2/Program blocks/Motion/Axis_FB`), its location (for example `Network 2`) and its description. `messageFilter` selects `Errors`, `ErrorsAndWarnings` (default) or `All`; `maxMessages` (default 200, 0 for no limit) caps the list, and `matchedMessageCount`/`truncated` tell whether more messages matched.

- `compilation_project` - Compile the entire project
- `compilation_software` - Compile the PLC software of a device, or one Software Unit with `unitName`
- `compilation_block` - Compile a single PLC block
- `compilation_hmi` - Compile the HMI software of a device

### File Handling
- `files_read_csv` - Read a CSV file
- `files_read_excel` - Read an Excel file
- `files_list_sheets` - List sheets in an Excel file
- `files_validate_format` - Validate file format
- `files_get_info` - Get file information

### HMI Targets
- `hmi_targets_list` - List HMI targets
- `hmi_targets_get` - Get HMI target details
- `hmi_targets_validate` - Validate HMI target configuration

### WinCC Unified HMI
Tools for WinCC Unified HMI software (Unified Comfort Panels and PC runtimes). Write tools support `dryRun` and change the open project in memory; call `projects_save` to persist the changes. Attributes are passed as a JSON object such as `{"Left": 20, "Top": 40, "Text": "Start"}`; numbers, booleans, enum member names and strings are converted to each attribute's type, colors are given as `#RRGGBB`, `#AARRGGBB`, `R,G,B`, `A,R,G,B` or a name such as `Red` (and are listed as `#RRGGBB` / `#AARRGGBB`), and for multilingual texts a plain value sets every language while `de-DE:Start` sets one.

- `hmi_unified_software_list` - List the WinCC Unified HMI devices with counts of screens, tags, tag tables, connections, text lists, graphic lists and system text lists
- `hmi_unified_screens_list` - List screens, including screens in screen groups
- `hmi_unified_screen_create` - Create a screen, optionally in a screen group
- `hmi_unified_screen_delete` - Delete a screen
- `hmi_unified_screens_snapshot` - Write a deterministic JSON review snapshot per screen for git diffs (see below)
- `hmi_unified_screen_items_list` - List the items of a screen with type, position and size (`includeAttributes=true` for all simple attributes)
- `hmi_unified_screen_item_types_list` - List the screen item types that can be created
- `hmi_unified_screen_item_create` - Create a screen item and set its attributes
- `hmi_unified_screen_item_set_attributes` - Set attributes of a screen item
- `hmi_unified_screen_item_delete` - Delete a screen item
- `hmi_unified_tag_tables_list` - List HMI tag tables
- `hmi_unified_tags_list` - List HMI tags, optionally by tag table and name/regex
- `hmi_unified_tag_create` - Create an internal or external (PLC-bound) HMI tag
- `hmi_unified_tag_delete` - Delete an HMI tag
- `hmi_unified_tags_export` - Export HMI tags per tag table to `<exportPath>\<tag table group path>\<table>\`: all tables, one table (`tagTableName`) or one tag (`tagName`). TIA Portal V21 cannot export all tags of a device at once
- `hmi_unified_tags_import` - Import such table directories back, each into the tag table of the same name; missing groups and tables are created
- `hmi_unified_connections_list` - List HMI connections
- `hmi_unified_textlists_list` - List text lists
- `hmi_unified_textlists_export` - Export all text lists with their entries to a file named after `fileName`
- `hmi_unified_textlists_import` - Import text lists from such a file
- `hmi_unified_graphiclists_list` - List graphic lists
- `hmi_unified_graphiclists_export` - Export all graphic lists with their entries to `<fileName>.hmi.yml` (YAML)
- `hmi_unified_graphiclists_import` - Import graphic lists from such a file
- `hmi_unified_systemtextlists_export` - Export all system text lists to `<fileName>.hmi.yml` and `<fileName>.TextLibrary.hmi.yml`
- `hmi_unified_systemtextlists_import` - Import system text lists from such a file. In TIA Portal V21 this import was seen to double `@@` escapes in texts; compare a fresh export before saving

For the list tools, `fileName` is a base name such as `GraphicLists`; TIA Portal adds its own extension. The imports accept the same base name or the name of the written `.hmi.yml` file.
- `hmi_unified_scripts_list` - List global script modules
- `hmi_unified_scripts_export` - Export all script modules, or one module, to files
- `hmi_unified_scripts_import` - Import script modules, or one file, from such files

#### Screen snapshots
WinCC Unified screens have no Openness export, so `hmi_unified_screens_snapshot(deviceName, exportPath, screenName = "")` reads them through the object model and writes one file per screen to `<exportPath>\Screens\<screen group path>\<screen>.json`, for review in git (snapshots cannot be imported):

- Every readable property of the screen and of each item, with an `@type` key per object; items stay in their z-order.
- Only objects that belong to the screen or item are read (their `Parent` is the object holding them). References to other engineering objects, such as another screen or a faceplate's library type, are written as `{ "@type": "<type>", "@ref": "<name>" }` without reading them. Each screen and item is logged before it is read, so the log file names the object if reading fails.
- Parts such as fonts as nested objects; dynamizations keyed by property, event handlers keyed by event, faceplate interfaces keyed by property; empty collections are left out.
- Colors as `#AARRGGBB`, enums by member name, multilingual texts as `{ "<culture>": "<text>" }`.
- Script code in sidecar files below `<screen>.scripts\`: `<item>.<event>.js` for event handlers (the screen's own events use the screen name), `<item>.<property>.dyn.js` for script dynamizations, with `.global` before the extension for global definition areas. The JSON references them as `{ "file": "<screen>.scripts/<file>" }`.
- Properties that cannot be read are listed per object under `@unreadable` and in the tool result.
- Sorted keys, UTF-8 without BOM, LF line endings and a final newline, so an unchanged screen gives identical files; files whose content is unchanged are not rewritten. Files of deleted or renamed screens and scripts are not removed; the tool returns every file of the run.

The same format covers the rest of the HMI configuration, with one file per collection, `{ "items": { "<name>": { ... } } }`, sorted by name:

- `hmi_unified_alarms_snapshot(deviceName, exportPath)` - `<exportPath>\Alarms\DiscreteAlarms.json`, `AnalogAlarms.json` and `AlarmClasses.json`, including trigger tags and bits, acknowledgment tags, alarm classes, priorities and multilingual alarm texts
- `hmi_unified_settings_snapshot(deviceName, exportPath)` - `<exportPath>\Settings\Connections.json` (with driver properties), `DataLogs.json`, `AlarmLogs.json`, `AuditTrails.json` and `RuntimeSettings.json`; collections a device does not support are reported in the result instead

### Sampling (LLM-assisted)
- `sampling_generate_code` - Generate code with LLM assistance
- `sampling_summarize_project` - Summarize the current project
- `sampling_get_suggestions` - Get suggestions based on context

### Project Management (Extended)
- `projects_get_session_info` - Get current session information
- `projects_open_with_upgrade` - Open a project with upgrade

## Usage with MCP Inspector

Test the server using MCP Inspector:
```bash
npx @modelcontextprotocol/inspector dotnet run
```

This will allow you to interactively test all available tools.

## Development Notes

- Built with .NET Framework 4.8 and ModelContextProtocol SDK
- Logging provided by Serilog (configured via appsettings.json)
- All logs written to stderr to maintain MCP protocol integrity on stdout, and to `logs\tiaportal-mcp.log` next to the exe (`appsettings.json` is read from the exe's directory, not the client's working directory)
- Full TIA integration requires Windows environment with TIA Portal installed
- Error handling maps TIA exceptions to MCP error codes


## Architecture

- Uses MCP SDK for stdio-based server
- Tools auto-discovered via `[McpServerToolType]` attributes
- Dependency injection with Microsoft.Extensions.Hosting
- **Logging:** Serilog configured to write to stderr (required for MCP protocol compatibility)
- **Project structure is organized by feature and concern:**
   - `Tools/` — MCP tool classes grouped by domain (ProjectManagement, Device, DeviceItem, Software, Tag, Blocks, XmlExchange, Cax, Compilation, File, HmiTarget, Utility, Sampling, UserInteraction, Test, Hardware)
   - `Services/` — TIA Portal adapters and session management (TiaPortalService, TiaPortalSessionManager, BlocksAdapter, HmiTargetAdapter, FileAdapter)
   - `Models/` — Data transfer objects and result models
   - `Extensions/` — DI and service collection helpers
- **Testing project:** `TiaPortalMcpServer.Tests` contains unit and integration tests

## Limitations

- Requires TIA Portal V20 on Windows
- No authentication beyond MCP protocol
- Some advanced block operations (e.g., WebServer page generation, loadable generation) are not yet exposed as tools

## Contributing

1. Follow the tool naming convention: `[namespace]_[action]` (e.g., `blocks_list`, `devices_create`)
2. Add proper error handling for TIA exceptions
3. Test with real TIA Portal instances
4. Add unit tests for new tools
5. Update this README when adding or modifying tools
