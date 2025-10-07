using Octokit;

public interface IGithubPoller
{
    public Task<IEnumerable<Repository?>?> GetRepositories();
}

public class GithubPoller : IGithubPoller
{
    private IEnumerable<Repository?>? Data = null;
    private GitHubClient client = new GitHubClient(new ProductHeaderValue("PersonalSite"));
    private DateTime refreshTime;
    private string username = "junming-qiu";

    public GithubPoller()
    {
        refreshTime = DateTime.Now;
    }

    public async Task<IEnumerable<Repository?>?> GetRepositories()
    {
        if (refreshTime > DateTime.Now)
        {
            Console.WriteLine("Cached");
            return Data;
        }
        else
        {
            Console.WriteLine("Retrieving new");
            Data = null;
        }

        await PullRepoData(username);
        return Data;
    }

    private async Task<bool> PullRepoData(string username)
    {
        try
        {
            var repositories = await client.Repository.GetAllForUser(username);
            Data = repositories;

            Console.WriteLine($"Repositories for {username}:");
            foreach (var repo in repositories)
            {
                Console.WriteLine($"- {repo.FullName} ({(repo.Private ? "private" : "public")})");
            }
            refreshTime = DateTime.Now.Add(new TimeSpan(0, 0, 0, 10));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching repositories: {ex.Message}");
        }

        return true;
    }
}