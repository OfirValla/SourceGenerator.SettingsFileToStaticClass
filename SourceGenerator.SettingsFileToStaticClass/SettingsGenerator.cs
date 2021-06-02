using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace SourceGenerator.SettingsFileToStaticClass
{
    [Generator]
    public class SettingsGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            CreateSettingsFile(context, false);
            CreateSettingsFile(context, true);
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            //if (!Debugger.IsAttached)
            //    Debugger.Launch();
        }

        private void CreateSettingsFile(GeneratorExecutionContext context, bool isInternal = false)
        {
            string className = isInternal ? "InternalSettings" : "Settings";
            string fileName = isInternal ? $"{context.Compilation.AssemblyName}.settings.json" : "settings.json";

            try
            {
                // Load the settings file
                var configurationDictionary = LoadConfigFile(context, fileName);
                if (configurationDictionary == null) return; // Did not find the config file

                // Find all the top level properties ( I.E. properties like string, int... )
                var topLevelProperties = configurationDictionary
                    .Where(dict => !(dict.Value.Value is Dictionary<string, KeyValuePair<List<string>, object>>))
                    .ToList();

                // Find all the complex properties ( I.E. sub classes )
                var complexProperties =
                    configurationDictionary.Except(topLevelProperties)
                        .ToList();

                
                var configSectionClasses = new StringBuilder();
                complexProperties.ForEach(config => BuildConfigClass(config, configSectionClasses));
                
                var sourceBuilder = new StringBuilder($@"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace {context.Compilation.AssemblyName}.Generated
{{
    public static class {className}
    {{");

                // Writing top level properties
                foreach (var item in topLevelProperties)
                {
                    var value = item.Value.Value;
                    var key = item.Key;

                    if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                    {
                        // Check first value to see what kind of array (list) it needs to be
                        var propertyType = GetPropType(element.EnumerateArray().FirstOrDefault());

                        sourceBuilder.Append(
                            $"\n\t\tpublic static List<{propertyType}> {NormalizePropertyName(key)} {{ get; private set; }}");
                    }
                    else
                    {
                        sourceBuilder.Append($"\n\t\tpublic static {GetPropType((JsonElement)value)} {NormalizePropertyName(key)} {{ get; private set; }}");
                    }
                }

                // Writing sub classes properties 
                foreach (var item in complexProperties)
                {
                    var key = item.Key;
                    sourceBuilder.Append(
                        $"\n\t\tpublic static {NormalizePropertyName(key)} {NormalizePropertyName(key)}{{ get; private set; }}");
                }

                // Writing the static ctor
                sourceBuilder.Append($"\n\n\t\tstatic {className}(){{");
                sourceBuilder.Append("\n\t\t\tvar assemblyLoc = System.Reflection.Assembly.GetExecutingAssembly().Location;");
                sourceBuilder.Append("\n\t\t\tvar directoryPath = System.IO.Path.GetDirectoryName(assemblyLoc);");
                sourceBuilder.Append($"\n\t\t\tvar configFilePath = System.IO.Path.Combine(directoryPath, \"{fileName}\");");

                sourceBuilder.Append("\n\t\t\tdynamic fileContent = JsonConvert.DeserializeObject<dynamic>(System.IO.File.ReadAllText(configFilePath));");
                foreach (var item in topLevelProperties)
                {
                    // MaxSendRetry = fileContent.MaxSendRetry;
                    var value = item.Value.Value;
                    var key = item.Key;

                    switch (((JsonElement)value).ValueKind)
                    {
                        case JsonValueKind.String:
                            sourceBuilder.Append($"\n\t\t\t{key} = fileContent.{key}.ToString();");
                            break;

                        case JsonValueKind.Number:
                        case JsonValueKind.Array:
                            sourceBuilder.Append($"\n\t\t\t{item.Key} = fileContent.{item.Key};");
                            break;
                    }
                }

                // Adding the nested classes
                foreach (var item in complexProperties)
                {
                    BuildStaticCtor(item, sourceBuilder);
                }

                sourceBuilder.Append("\n\t\t}");

                // Close settings class
                sourceBuilder.AppendLine("\n\t}\n");

                // Add records
                sourceBuilder.Append(configSectionClasses.ToString());

                // Close namespace
                sourceBuilder.Append("}");

                // Add the settings file to the source tree
                context.AddSource($"{className}Generated.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
            }
            catch {}
        }

        /// <summary>
        /// Loads the json file and deserialize it into a dictionary
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fileName">The file we want to search for</param>
        /// <returns></returns>
        private static Dictionary<string, KeyValuePair<List<string>, object>> LoadConfigFile(GeneratorExecutionContext context, string fileName)
        {
            var files = context.AdditionalFiles.Where(file => Path.GetFileName(file.Path).Equals(fileName)).ToList();
            if (!files.Any())
                return null;

            string contentOfFile = files.First()?.GetText()?.ToString();
            return DeserializeToTreeDict(contentOfFile, new List<string>());
        }

        /// <summary>
        /// Deserializes the given file content/json into a tree of key and values
        /// </summary>
        /// <param name="fileContent"></param>
        /// <param name="currentPath"></param>
        /// <returns></returns>
        private static Dictionary<string, KeyValuePair<List<string>, object>> DeserializeToTreeDict(string fileContent, List<string> currentPath)
        {
            // Allow comments in the json file ( for not breaking the deserialization )
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            var configValues = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fileContent, options);

            var deserializedConfig = new Dictionary<string, KeyValuePair<List<string>, object>>();
            foreach (KeyValuePair<string, JsonElement> configValue in configValues)
            {
                if (configValue.Value.ValueKind is JsonValueKind.Object)
                {
                    List<string> newPath = new List<string>(currentPath)
                    {
                        configValue.Key
                    };
                    deserializedConfig.Add(
                        configValue.Key, 
                        new KeyValuePair<List<string>, object>(
                            newPath, 
                            DeserializeToTreeDict(configValue.Value.ToString(), newPath)
                        )
                    );
                }
                else
                {
                    List<string> newPath = new List<string>(currentPath)
                    {
                        configValue.Key
                    };
                    deserializedConfig.Add(
                        configValue.Key, 
                        new KeyValuePair<List<string>, object>(newPath, configValue.Value)
                    );
                }
            }

            return deserializedConfig;
        }

        /// <summary>
        /// Converts an object which is represented in the classInfo as a tree of KeyValuePair into a string an writes it to the string builder
        /// Note: This creates records instead of classes
        /// </summary>
        /// <param name="classInfo"></param>
        /// <param name="sb"></param>
        private static void BuildConfigClass(KeyValuePair<string, KeyValuePair<List<string>, object>> classInfo, StringBuilder sb)
        {
            StringBuilder nestedClasses = new StringBuilder();

            sb.AppendLine($"\tpublic record {classInfo.Key}");
            sb.Append("\t(");

            foreach (var item in (Dictionary<string, KeyValuePair<List<string>, object>>)classInfo.Value.Value)
            {
                if (item.Value.Value is Dictionary<string, KeyValuePair<List<string>, object>>)
                {
                    sb.Append($"\n\t\t{item.Key} {NormalizePropertyName(item.Key)},");
                    BuildConfigClass(item, nestedClasses);
                }
                else
                {
                    var prop = (JsonElement)item.Value.Value;
                    var propertyType = GetPropType(prop);
                    if (prop.ValueKind == JsonValueKind.Array)
                    {
                        // Check first value to see what kind of array (list) it needs to be
                        propertyType = GetPropType(prop.EnumerateArray().FirstOrDefault());
                        sb.Append(
                            $"\n\t\tList<{propertyType}> {NormalizePropertyName(item.Key)},");
                    }
                    else
                    {
                        sb.Append($"\n\t\t{propertyType} {NormalizePropertyName(item.Key)},");
                    }
                }
            }
            sb.Remove(sb.Length - 1, 1);

            sb.Append("\n\t);\n");

            sb.Append(nestedClasses.ToString());
        }

        /// <summary>
        /// Removes . and $ from the property name and replaces them with _
        /// </summary>
        /// <param name="originalName"></param>
        /// <returns></returns>
        private static string NormalizePropertyName(string originalName)
        {
            const string underscore = "_";
            return originalName
                .Replace(".", underscore)
                .Replace("$", underscore);
        }

        private static string GetPropType(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    return "int";

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return "bool";

                default:
                    return "string";
            }
        }

        /// <summary>
        /// Generates a static ctor for the settings file
        /// </summary>
        /// <param name="classInfo"></param>
        /// <param name="sb"></param>
        /// <param name="isNestedCtor"></param>
        /// <param name="depth"></param>
        public static void BuildStaticCtor(KeyValuePair<string, KeyValuePair<List<string>, object>> classInfo, StringBuilder sb, bool isNestedCtor = false, int depth = 3)
        {
            // Append the call of the ctor
            string c = isNestedCtor ? ":" : "=";
            sb.Append($"\n{new string('\t', depth)}{classInfo.Key} {c} new {classInfo.Key}(");

            foreach (var item in (Dictionary<string, KeyValuePair<List<string>, object>>)classInfo.Value.Value)
            {
                // Sub class
                if (item.Value.Value is Dictionary<string, KeyValuePair<List<string>, object>>)
                {
                    BuildStaticCtor(item, sb, true, depth + 1);
                }
                else
                {
                    switch (((JsonElement)item.Value.Value).ValueKind)
                    {
                        case JsonValueKind.String:
                            sb.Append($"\n{new string('\t', depth + 1)}{item.Key}: fileContent.{string.Join(".", item.Value.Key)}.ToString(),");
                            break;

                        case JsonValueKind.Number:
                        case JsonValueKind.Array:
                            sb.Append($"\n{new string('\t', depth + 1)}{item.Key}: fileContent.{string.Join(".", item.Value.Key)},");
                            break;
                    }

                }
            }

            // Remove the last ,
            sb.Remove(sb.Length - 1, 1);

            // Close the ctor call
            c = isNestedCtor ? "," : ";";
            sb.Append($"\n{new string('\t', depth)}){c}");
        }
    }
}
