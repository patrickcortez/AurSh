using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace AurShell.Gpm;

public class GpmNetworkService
{
    public async Task<List<string>> SearchRepositoriesAsync(string query)
    {
        var results = new List<string>();
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AurSh-GPM", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            string url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&per_page=10";
            
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"gpm: GitHub API returned {response.StatusCode}");
                return results;
            }

            string content = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(content);
            
            if (doc.RootElement.TryGetProperty("items", out JsonElement items))
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("full_name", out JsonElement fullName))
                    {
                        results.Add(fullName.GetString() ?? string.Empty);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"gpm: Failed to search GitHub: {ex.Message}");
        }

        return results;
    }
}
