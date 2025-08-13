# Implementation Plan

- [x] 1. Rename primary project files and update solution configuration

  - Rename FolderDupFinder.csproj to ClutterFlock.csproj
  - Rename FolderDupFinder.sln to ClutterFlock.sln
  - Rename FolderDupFinder.code-workspace to ClutterFlock.code-workspace
  - Update solution file to reference ClutterFlock.csproj instead of FolderDupFinder.csproj
  - Update workspace file to reference new project name
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. Update project configuration and build settings

  - Update AssemblyName and RootNamespace in ClutterFlock.csproj to use ClutterFlock
  - Update OutputType and other project properties to reference ClutterFlock
  - Verify that build output will generate ClutterFlock.exe instead of FolderDupFinder.exe
  - _Requirements: 6.1, 6.2, 6.3_

- [x] 3. Update all C# namespace declarations throughout the codebase

  - Update namespace FolderDupFinder to ClutterFlock in App.xaml.cs
  - Update namespace FolderDupFinder to ClutterFlock in MainWindow.xaml.cs
  - Update namespace FolderDupFinder.Core to ClutterFlock.Core in all Core/\*.cs files
  - Update namespace FolderDupFinder.Models to ClutterFlock.Models in Models/DataModels.cs
  - Update namespace FolderDupFinder.Services to ClutterFlock.Services in Services/Interfaces.cs
  - Update namespace FolderDupFinder.ViewModels to ClutterFlock.ViewModels in ViewModels/MainViewModel.cs
  - _Requirements: 2.1, 2.4_

- [x] 4. Update all using statements and internal references

  - Update using FolderDupFinder statements to using ClutterFlock throughout all .cs files
  - Update using FolderDupFinder.Core statements to using ClutterFlock.Core in all files
  - Update using FolderDupFinder.Models statements to using ClutterFlock.Models in all files
  - Update using FolderDupFinder.Services statements to using ClutterFlock.Services in all files
  - Update using FolderDupFinder.ViewModels statements to using ClutterFlock.ViewModels in all files
  - _Requirements: 2.3, 2.4_

- [x] 5. Update XAML files with new namespace references

  - Update x:Class="FolderDupFinder.App" to x:Class="ClutterFlock.App" in App.xaml
  - Update x:Class="FolderDupFinder.MainWindow" to x:Class="ClutterFlock.MainWindow" in MainWindow.xaml
  - Update xmlns:local="clr-namespace:DuplicateFinder" to xmlns:local="clr-namespace:ClutterFlock" in App.xaml
  - Update any other XAML namespace references from FolderDupFinder to ClutterFlock
  - _Requirements: 2.2, 2.4_

- [x] 6. Update user interface text and window titles

  - Update Title="Folder Duplicates Finder" to Title="ClutterFlock" in MainWindow.xaml
  - Update any hardcoded "FolderDupFinder" or "Folder Duplicates Finder" strings in XAML to "ClutterFlock"
  - Update window title and any user-visible application name references
  - _Requirements: 3.1, 3.2_

- [x] 7. Update file dialog filters and project file extensions

  - Update file dialog filter from "Duplicate Folder Project (_.dfp)|_.dfp" to "ClutterFlock Project (_.cfp)|_.cfp|Legacy Project (_.dfp)|_.dfp"
  - Update default file extension from .dfp to .cfp in save dialogs
  - Ensure backward compatibility by supporting both .cfp and .dfp file loading
  - Update any hardcoded file extension references in the code

  - _Requirements: 3.3, 5.1, 5.2, 5.3, 8.1, 8.2_

- [x] 8. Update project data model to include application identification

  - Add ApplicationName property to ProjectData model with default value "ClutterFlock"
  - Add LegacyApplicationName property for migration tracking
  - Update project save functionality to include new application identification fields
  - Ensure project loading handles both legacy and new format files
  - _Requirements: 5.4, 8.3, 8.4_

- [x] 9. Update README and main documentation

  - Update project title from "Folder duplicates finder" to "ClutterFlock" in README.md
  - Update all references to FolderDupFinder in README content to ClutterFlock
  - Update screenshot references and application descriptions
  - Update any installation or usage instructions to reference ClutterFlock
  - _Requirements: 4.1, 4.2_

- [x] 10. Update steering files and technical documentation

  - Update .kiro/steering/tech.md to reference ClutterFlock instead of FolderDupFinder
  - Update .kiro/steering/structure.md project structure documentation
  - Update .kiro/steering/requirements.md to reference ClutterFlock
  - Update .kiro/steering/product.md product overview and branding
  - _Requirements: 4.3_

- [x] 11. Update existing specification documents

  - Update .kiro/specs/documentation-and-enhancements/requirements.md to reference ClutterFlock
  - Update .kiro/specs/documentation-and-enhancements/design.md to reference ClutterFlock
  - Update any other specification documents that reference the old name
  - _Requirements: 4.4_

- [x] 12. Update assembly information and metadata

  - Update AssemblyInfo.cs or add assembly attributes to reference ClutterFlock
  - Update assembly title, description, and product name to ClutterFlock
  - Ensure assembly metadata reflects the new application name
  - _Requirements: 6.4_

- [x] 13. Test compilation and build process

  - Verify that the project compiles successfully with all namespace changes
  - Test that the build generates ClutterFlock.exe with correct metadata
  - Verify that all references are resolved and no compilation errors exist
  - Test that the application runs correctly with the new name
  - _Requirements: 6.1, 6.2_

- [x] 15. Validate user interface and branding consistency

  - Test that all windows and dialogs display "ClutterFlock" correctly
  - Verify that file dialogs show correct filters and extensions
  - Test that status messages and user-visible text use consistent branding
  - Ensure no remnants of old FolderDupFinder branding remain visible
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

