using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace DotNetOSSIndex
{
    [Command(Name = "dotnet oss-index", FullName = "A .NET Core global tool to list vulnerable Nuget packages.")]
    class Program
    {
        [Option(Description = "The path to the project file", ShortName = "p")]
        public string Project { get; }

        public static int Main(string[] args)
            => CommandLineApplication.Execute<Program>(args);

        public async Task<int> OnExecute()
        {
            var defaultForegroundColor = Console.ForegroundColor;

            Console.WriteLine();

            if (string.IsNullOrEmpty(Project))
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"Option --project is required");

                Console.ForegroundColor = defaultForegroundColor;

                return 1;
            }

            if (!File.Exists(Project))
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"Project file \"{Project}\" does not exist");

                Console.ForegroundColor = defaultForegroundColor;

                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Blue;

            Console.WriteLine($"» Project: {Project}");

            Console.ForegroundColor = defaultForegroundColor;

            Console.WriteLine();
            Console.WriteLine("  Getting packages".PadRight(64));
            Console.SetCursorPosition(Console.CursorLeft, Console.CursorTop - 1);

            var coordinates = new List<string>();

            try
            {
                using (XmlReader reader = XmlReader.Create(Project))
                {
                    while (reader.Read())
                    {
                        if (reader.IsStartElement())
                        {
                            switch (reader.Name)
                            {
                                case "PackageReference":
                                    var packageName = reader["Include"];
                                    var packageVersion = reader["Version"];

                                    coordinates.Add($"pkg:nuget/{packageName}@{packageVersion}");

                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            
                Console.WriteLine($"  An unhandled exception occurred while getting the packages: {ex.Message}");

                Console.ForegroundColor = defaultForegroundColor;

                return 1;
            }

            Console.WriteLine("  Checking for vulnerabilities".PadRight(64));
            Console.SetCursorPosition(Console.CursorLeft, Console.CursorTop - 1);

            var client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var request = new
            {
                coordinates = coordinates
            };

            var requestAsString = JsonConvert.SerializeObject(request);
            var requestAsStringContent = new StringContent(requestAsString, Encoding.UTF8, "application/json");

            HttpResponseMessage response;

            try
            {
                response = await client.PostAsync("https://ossindex.sonatype.org/api/v3/component-report", requestAsStringContent);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            
                Console.WriteLine($"  An unhandled exception occurred while checking for vulnerabilities: {ex.Message}");

                Console.ForegroundColor = defaultForegroundColor;

                return 1;
            }

            var contentAsString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            
                Console.WriteLine($"  An unhandled exception occurred while checking for vulnerabilities: {contentAsString}");

                Console.ForegroundColor = defaultForegroundColor;

                return 1;
            }

            Component[] components;

            try
            {
                components = JsonConvert.DeserializeObject<Component[]>(contentAsString);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            
                Console.WriteLine($"  An unhandled exception occurred while checking for vulnerabilities: {ex.Message}");

                Console.ForegroundColor = defaultForegroundColor;

                return 1;
            }

            var affectedComponents = components.Where(c => c.Vulnerabilities.Length > 0);

            Console.WriteLine($"  {affectedComponents.Count()} packages affected".PadRight(64));
            Console.WriteLine();

            foreach (var component in affectedComponents)
            {
                Console.WriteLine($"          Package: {component.Coordinates}");
                Console.WriteLine($"        Reference: {component.Reference}");
                Console.Write(     "  Vulnerabilities:");

                foreach (var vulnerability in component.Vulnerabilities.OrderByDescending(v => v.CVSSScore))
                {
                    Console.SetCursorPosition(19, Console.CursorTop);

                    var severity = "NONE";
                    var severityForegroundColor = defaultForegroundColor;

                    if (vulnerability.CVSSScore >= 0.1 && vulnerability.CVSSScore <= 3.9)
                    {
                        severity = "LOW";
                    }

                    if (vulnerability.CVSSScore >= 4.0 && vulnerability.CVSSScore <= 6.9)
                    {
                        severity = "MEDIUM";
                        severityForegroundColor = ConsoleColor.Yellow;
                    }

                    if (vulnerability.CVSSScore >= 7.0 && vulnerability.CVSSScore <= 8.9)
                    {
                        severity = "HIGH";
                        severityForegroundColor = ConsoleColor.Red;
                    }

                    if (vulnerability.CVSSScore >= 9.0)
                    {
                        severity = "CRITICAL";
                        severityForegroundColor = ConsoleColor.Red;
                    }

                    Console.ForegroundColor = severityForegroundColor;

                    Console.WriteLine("- {0,-8} {1}", severity, vulnerability.Title);

                    Console.ForegroundColor = defaultForegroundColor;
                }

                Console.WriteLine();
            }

            return 0;
        }
    }

    class Component
    {
        public string Coordinates { get; set; }

        public string Description { get; set; }

        public string Reference { get; set; }

        public Vulnerability[] Vulnerabilities { get; set; }
    }

    class Vulnerability
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public float CVSSScore { get; set; }

        public string CVSSVector { get; set; }

        public string CWE { get; set; }

        public string Reference { get; set; }

        public string VersionRanges { get; set; }
    }
}