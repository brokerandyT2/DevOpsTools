using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Discovery
{
    /// <summary>
    /// Discovery engine for C# projects. Analyzes compiled .NET assemblies to find classes.
    /// </summary>
    public class CSharpDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<CSharpDiscoveryEngine> _logger;

        public CSharpDiscoveryEngine(ILogger<CSharpDiscoveryEngine> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting C# discovery process using assembly analysis...");
            var discoveredClasses = new List<DiscoveredClass>();

            try
            {
                var assemblies = await LoadAssembliesAsync(config);
                _logger.LogInformation("Found {Count} assemblies to analyze.", assemblies.Count);

                foreach (var assembly in assemblies)
                {
                    var classesInAssembly = await AnalyzeAssemblyAsync(assembly, config);
                    discoveredClasses.AddRange(classesInAssembly);
                }

                return discoveredClasses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A critical error occurred during C# class discovery.");
                throw new MobileAdapterException(MobileAdapterExitCode.DiscoveryFailure, "C# discovery failed.", ex);
            }
        }

        private async Task<List<Assembly>> LoadAssembliesAsync(GeneratorConfiguration config)
        {
            var assemblies = new List<Assembly>();
            var searchPaths = new List<string>();

            if (!string.IsNullOrEmpty(config.Assembly.CoreAssemblyPath)) searchPaths.Add(config.Assembly.CoreAssemblyPath);
            if (!string.IsNullOrEmpty(config.Assembly.TargetAssemblyPath)) searchPaths.Add(config.Assembly.TargetAssemblyPath);
            if (!string.IsNullOrEmpty(config.Assembly.SearchFolders))
            {
                searchPaths.AddRange(config.Assembly.SearchFolders.Split(';').Select(p => p.Trim()));
            }

            if (searchPaths.Count == 0)
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "No C# assembly search paths were provided.");
            }

            foreach (var path in searchPaths.Where(Directory.Exists))
            {
                var pattern = string.IsNullOrEmpty(config.Assembly.AssemblyPattern) ? "*.dll" : config.Assembly.AssemblyPattern;
                var assemblyFiles = Directory.GetFiles(path, pattern, SearchOption.AllDirectories);

                foreach (var file in assemblyFiles)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(file);
                        assemblies.Add(assembly);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load assembly from {File}. It may be a non-.NET DLL or have missing dependencies.", file);
                    }
                }
            }

            await Task.CompletedTask; // To maintain async signature
            return assemblies;
        }

        private async Task<List<DiscoveredClass>> AnalyzeAssemblyAsync(Assembly assembly, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            try
            {
                var types = assembly.GetTypes().Where(t => IsMatch(t, config));

                foreach (var type in types)
                {
                    classes.Add(AnalyzeType(type));
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogWarning(ex, "Could not load all types from assembly {Assembly}. Some types will be skipped.", assembly.FullName);
                foreach (var loaderEx in ex.LoaderExceptions)
                {
                    _logger.LogDebug("Loader Exception: {Message}", loaderEx?.Message);
                }
            }

            await Task.CompletedTask; // To maintain async signature
            return classes;
        }

        private bool IsMatch(Type type, GeneratorConfiguration config)
        {
            if (!type.IsClass || type.IsAbstract) return false;

            if (!string.IsNullOrEmpty(config.TrackAttribute))
            {
                return type.GetCustomAttributes(false).Any(attr => attr.GetType().Name.Contains(config.TrackAttribute));
            }
            if (!string.IsNullOrEmpty(config.TrackNamespace))
            {
                return type.Namespace?.StartsWith(config.TrackNamespace) ?? false;
            }
            // Other discovery methods like pattern can be added here
            return false;
        }

        private DiscoveredClass AnalyzeType(Type type)
        {
            var discoveredClass = new DiscoveredClass
            {
                Name = type.Name,
                Namespace = type.Namespace ?? string.Empty,
                Properties = new List<DiscoveredProperty>(),
                Methods = new List<DiscoveredMethod>()
            };

            // Analyze properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var discoveredProp = new DiscoveredProperty
                {
                    Name = prop.Name,
                    Type = GetTypeName(prop.PropertyType)
                };
                // Further analysis like collection element type can be added here
                discoveredClass.Properties.Add(discoveredProp);
            }

            // Analyze methods
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
            {
                var discoveredMethod = new DiscoveredMethod
                {
                    Name = method.Name,
                    ReturnType = GetTypeName(method.ReturnType),
                    Parameters = method.GetParameters().Select(p => new DiscoveredParameter
                    {
                        Name = p.Name,
                        Type = GetTypeName(p.ParameterType)
                    }).ToList()
                };
                discoveredClass.Methods.Add(discoveredMethod);
            }

            return discoveredClass;
        }

        private string GetTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var genericTypeName = type.Name.Substring(0, type.Name.IndexOf('`'));
                var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetTypeName));
                return $"{genericTypeName}<{genericArgs}>";
            }
            return type.Name;
        }
    }
}