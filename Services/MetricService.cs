using Avalonia.Controls;
using DexInstructionRunner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DexInstructionRunner.Services
{
    public class MeasureService
    {
        private readonly HttpClient _httpClient;
        private readonly TextBox? _logTextBox;

        public MeasureService(HttpClient httpClient, TextBox? logTextBox = null)
        {
            _httpClient = httpClient;
            _logTextBox = logTextBox;
        }

        private void Log(string message)
        {
            if (_logTextBox != null)
                _logTextBox.Text += message + "\n";
        }

        public async Task<List<ExperienceMeasure>> GetExperienceMeasuresAsync(string baseUrl, string token)
        {
            try
            {
                if (_logTextBox != null)
                    _logTextBox.Text += "📞 MeasureService called!\n";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", token);

                var url = $"https://{baseUrl.Trim().TrimEnd('/')}/Experience/devicemetrics/metadata";

                // Log API request using ApiLogger
                string payloadJson = "{}"; // Placeholder payload, as no payload is needed for GET requests
                await ApiLogger.LogApiCallAsync("FetchExperienceMeasures", "/Experience/devicemetrics/metadata", async () =>
                {
                    var response = await _httpClient.GetAsync(url);
                    return await response.Content.ReadAsStringAsync();  // Convert the response to string here
                }, payloadJson);

                Log($"📡 Fetching measures from: {url}");

                var response = await _httpClient.GetAsync(url);
                Log($"📡 Response status: {(int)response.StatusCode} {response.StatusCode}");

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();

                // Log the raw JSON response to check the structure
                //  Log($"🔍 Raw JSON response: {json}");

                // Deserialize the response into a list of ExperienceMeasure objects (including children)
                var measures = JsonSerializer.Deserialize<List<ExperienceMeasure>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    MaxDepth = 20 // Increase depth to handle nested structures
                });

                // Check how many measures were successfully parsed
                if (measures == null || measures.Count == 0)
                {
                    Log("❌ No measures found in the response.");
                    return new List<ExperienceMeasure>(); // Return empty if no measures found
                }

                // Log the number of measures parsed
                Log($"🔍 Number of measures parsed: {measures.Count}");

                // Separate the measures into two categories: Attributes (for GroupBy) and Measures (for Results)
                var groupByAttributes = new List<ExperienceMeasure>();
                var resultMeasures = new List<ExperienceMeasure>();

                // Process each measure and classify them based on BadgeType
                foreach (var measure in measures)
                {
                    if (measure.BadgeType == "Attribute") // Add to group by attributes
                    {
                        groupByAttributes.Add(measure);
                    }
                    else if (measure.BadgeType.Contains("Metric")) // Add to results (measurements)
                    {
                        resultMeasures.Add(measure);
                    }

                    // Process the metadata and children if any
                    ProcessMeasure(measure);
                    if (measure.Children != null && measure.Children.Any())
                    {
                        foreach (var child in measure.Children)
                        {
                            ProcessMeasure(child); // Recursively process each child measure
                        }
                    }
                }

                // Log and return the separated lists
                Log($"✅ Parsed {resultMeasures.Count} result measures and {groupByAttributes.Count} attributes for grouping.");

                // Optionally, you can return both groups (Attributes and Measures) or log them.
                // You can process groupByAttributes separately in the UI to handle the "Group By" logic.
                return resultMeasures; // Return the list of result measures for further processing
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to fetch or parse measures: {ex.Message}");
                return new List<ExperienceMeasure>(); // Return empty list on error
            }
        }


        // Method to process a single measure and its details
        private void ProcessMeasure(ExperienceMeasure measure)
        {
            // If Metadata or Notes is missing, set it to "No description available"
            string notes = measure?.Metadata?.Notes != null && measure.Metadata.Notes.Any()
                ? string.Join(", ", measure.Metadata.Notes)
                : "No description available"; // Default if Notes is null

            // Log the measure details, including Notes
            //   Log($"🔍 Measure ID: {measure.Id}, Name: {measure.Name}, Title: {measure.Title}, Unit: {measure.Unit}, DataType: {measure.DataType}");
            // Log($"🔍 Notes: {notes}");
            //  Log($"🔍 Measures: {string.Join(", ", measure.Measures ?? new List<string>())}, BadgeType: {measure.BadgeType}");

            // Process the metadata of the measure if it's available
            if (measure.Metadata != null)
            {
                // Log the metadata for the measure
                //    Log($"🔍 Metadata ID: {measure.Metadata.Id}, Name: {measure.Metadata.Name}, Title: {measure.Metadata.Title}");

                // Process each ID in Metadata and its children if any
                ProcessMetadataIds(measure.Metadata);


                // Log the full serialized Metadata (optional)
                //  Log($"🔍 Full Metadata: {JsonSerializer.Serialize(measure.Metadata)}");
            }

            // Process the weight of the measure (if available)
            if (measure.Weight != null)
            {
                if (measure.Weight.Default != 0)
                {
                    // Log($"🔍 Weight: Default = {measure.Weight.Default}");
                }
                else
                {
                    //   Log($"🔍 Weight: Default is zero.");
                }
            }
            else
            {
                //Log($"🔍 Weight is null.");
            }

            // Log InvestigationCategories if any
            if (measure.InvestigationCategories != null && measure.InvestigationCategories.Any())
            {
                //  Log($"🔍 Investigation Categories: {string.Join(", ", measure.InvestigationCategories)}");
            }

            // Process the children recursively if they exist
            if (measure.Children != null && measure.Children.Any())
            {
                foreach (var child in measure.Children)
                {
                    ProcessMeasure(child); // Recursively process each child measure
                }
            }
        }

        // Method to process all IDs inside the Metadata branch, including nested children if they exist
        private void ProcessMetadataIds(Metadata metadata)
        {
            // Log the ID for the current Metadata object
            Log($"🔍 Metadata ID: {metadata.Id}");

            // If there are children in the metadata, process them recursively
            if (metadata.Children != null && metadata.Children.Any())
            {
                foreach (var child in metadata.Children)
                {
                    // Log the ID for the child
                    Log($"🔍 Processing Child Metadata ID: {child.Id}");

                    // Recursively process each child
                    ProcessMetadataIds(child);
                }
            }
        }


    }
}

