using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Handles project persistence operations
    /// </summary>
    public class ProjectManager : IProjectManager
    {
        private const string ApplicationName = "ClutterFlock";
        
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task SaveProjectAsync(string filePath, ProjectData projectData)
        {
            try
            {
                // Always set the current application name
                projectData.ApplicationName = ApplicationName;

                var json = JsonSerializer.Serialize(projectData, JsonOptions);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save project: {ex.Message}", ex);
            }
        }

        public async Task<ProjectData> LoadProjectAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Project file not found: {filePath}");

                var json = await File.ReadAllTextAsync(filePath);
                var projectData = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions);
                
                if (projectData == null)
                    throw new InvalidDataException("Invalid project file format");

                // Handle legacy project files that don't have application identification fields
                if (string.IsNullOrEmpty(projectData.ApplicationName))
                {
                    projectData.ApplicationName = ApplicationName;
                }

                return projectData;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load project: {ex.Message}", ex);
            }
        }

        public bool IsValidProjectFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                
                var extension = Path.GetExtension(filePath);
                if (!extension.Equals(".cfp", StringComparison.OrdinalIgnoreCase) && 
                    !extension.Equals(".dfp", StringComparison.OrdinalIgnoreCase)) 
                    return false;

                var json = File.ReadAllText(filePath);
                var projectData = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions);
                return projectData != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
