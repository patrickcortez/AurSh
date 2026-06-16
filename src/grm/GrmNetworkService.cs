using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace AurShell.Grm;

public class GrmNetworkService
{
    private HttpClient CreateClient(string? token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AurSh-GRM", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public async Task<List<string>> SearchRepositoriesAsync(string query, string? token = null)
    {
        var results = new List<string>();
        try
        {
            using var client = CreateClient(token);
            string url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&per_page=10";
            
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"grm: GitHub API returned {response.StatusCode}");
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
            Console.Error.WriteLine($"grm: Failed to search GitHub: {ex.Message}");
        }

        return results;
    }

    public async Task<string?> GetRepoInfoAsync(string repoName, string? token = null)
    {
        try
        {
            using var client = CreateClient(token);
            string url = $"https://api.github.com/repos/{repoName}";
            
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"grm: GitHub API returned {response.StatusCode} for {repoName}");
                return null;
            }

            string content = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(content);
            
            var root = doc.RootElement;
            string description = root.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null 
                ? desc.GetString() ?? "No description" : "No description";
            int stars = root.TryGetProperty("stargazers_count", out var st) ? st.GetInt32() : 0;
            int forks = root.TryGetProperty("forks_count", out var fk) ? fk.GetInt32() : 0;
            
            string license = "No license";
            if (root.TryGetProperty("license", out var lic) && lic.ValueKind != JsonValueKind.Null)
            {
                if (lic.TryGetProperty("name", out var licName))
                {
                    license = licName.GetString() ?? "No license";
                }
            }

            return $@"Repository: {repoName}
Description: {description}
Stars: {stars}
Forks: {forks}
License: {license}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Failed to fetch info for {repoName}: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetRepoReadmeAsync(string repoName, string? token = null)
    {
        try
        {
            using var client = CreateClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));
            
            string url = $"https://api.github.com/repos/{repoName}/readme";
            var response = await client.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                return null; // README might not exist, just return null silently
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Failed to fetch readme for {repoName}: {ex.Message}");
            return null;
        }
    }
}
