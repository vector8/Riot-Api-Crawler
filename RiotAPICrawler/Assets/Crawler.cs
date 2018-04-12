using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.IO;

public class Crawler : MonoBehaviour
{
    public string apiKey;
    public long accountIdSeed;

    public HashSet<string> matches;
    public Queue<string> matchesToProcess;
    public HashSet<string> accounts;
    public Queue<string> accountsToProcess;

    public int maxRequestsPerMinute = 40;
    public int maxMatchesToCrawl = 100000;

    public Text debugText;
    public Text crawlButtonText;
    public GameObject loadingText;
    public InputField apikeyField;
    public InputField crawledDataFolder;

    private float requestsCompletedInTheLastMinute = 0;
    private float timeElapsed = 0;

    private int matchesCrawled = 0;

    private bool crawling = false;

    // Use this for initialization
    void Start()
    {
        matches = new HashSet<string>();
        matchesToProcess = new Queue<string>();
        accounts = new HashSet<string>();
        accountsToProcess = new Queue<string>();

        accounts.Add(accountIdSeed.ToString());
        accountsToProcess.Enqueue(accountIdSeed.ToString());
    }

    public void startOrStopCrawl()
    {
        if(!crawling)
        {
            crawling = true;
            crawlButtonText.text = "Stop";
            debugLog("Starting crawl...");
            StartCoroutine(crawl());
        }
        else
        {
            crawling = false;
            crawlButtonText.text = "Start";
            debugLog("Stopping crawl...");
        }
    }

    public void apiKeyValueChanged(string key)
    {
        apiKey = apikeyField.text;
    }

    public void loadPreviouslyCrawledData()
    {
        loadingText.SetActive(true);
        matches.Clear();
        matchesToProcess.Clear();
        accounts.Clear();
        accountsToProcess.Clear();
        matchesCrawled = 0;
        timeElapsed = 0;
        requestsCompletedInTheLastMinute = 0;

        StartCoroutine(loadDataAsync());
    }

    private IEnumerator loadDataAsync()
    {
        foreach (string fileName in Directory.GetFiles(crawledDataFolder.text, "Match*"))
        {
            string matchID = fileName.Substring(crawledDataFolder.text.Length + 6, fileName.Length - crawledDataFolder.text.Length - 10);

            if(!File.Exists(crawledDataFolder.text + "/Timeline" + matchID + ".txt"))
            {
                debugLog("File Timeline" + matchID + ".txt does not exist. Deleting match file.");

                File.Delete(fileName);
                continue;
            }

            string matchBody = File.ReadAllText(fileName);

            matches.Add(matchID);

            // parse this match to get a list of account IDs that participated in the match
            string[] delimiters = { "\"accountId\":" };
            string[] tokens = matchBody.Split(delimiters, System.StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < tokens.Length; i++)
            {
                string participantAccountID = tokens[i].Substring(0, tokens[i].IndexOf(','));

                if (!accounts.Contains(participantAccountID))
                {
                    debugLog("Adding account id: " + participantAccountID + " from match: " + matchID);
                    accounts.Add(participantAccountID);
                    accountsToProcess.Enqueue(participantAccountID);
                }
            }

            matchesCrawled++;
            //debugLog("Done loading " + fileName);
            yield return null;
        }

        debugLog("Loaded " + matchesCrawled + " matches from file.");

        loadingText.SetActive(false);

        yield return null;
    }

    // Update is called once per frame
    void Update()
    {
        if(crawling)
        {
            timeElapsed += Time.deltaTime;

            if (timeElapsed > 60)
            {
                timeElapsed = 0;
                requestsCompletedInTheLastMinute = 0;
                debugLog("Resetting request count.");
            }
        }
    }

    private IEnumerator crawl()
    {
        while (accountsToProcess.Count > 0)
        {
            string currentAccount = accountsToProcess.Dequeue();

            string url = "https://na1.api.riotgames.com/lol/match/v3/matchlists/by-account/" + currentAccount + "/recent?api_key=" + apiKey;
            string recentMatches = "";
            yield return StartCoroutine(request(url, result => recentMatches = result));

            if (!string.IsNullOrEmpty(recentMatches))
            {
                // parse recent matches of this account id, add all unvisited match ids to matchesToProcess
                string[] delimiters = { "\"gameId\":" };
                string[] tokens = recentMatches.Split(delimiters, System.StringSplitOptions.RemoveEmptyEntries);

                for (int i = 1; i < tokens.Length; i++)
                {
                    string matchId = tokens[i].Substring(0, tokens[i].IndexOf(','));

                    if (!matches.Contains(matchId))
                    {
                        matches.Add(matchId);
                        matchesToProcess.Enqueue(matchId);
                    }
                }

                while (matchesToProcess.Count > 0)
                {
                    string currentMatchID = matchesToProcess.Dequeue();

                    yield return crawlMatch(currentMatchID);

                    string matchTimelineUrl = "https://na1.api.riotgames.com/lol/match/v3/timelines/by-match/" + currentMatchID + "?api_key=" + apiKey;
                    string matchTimeline = "";
                    yield return StartCoroutine(request(matchTimelineUrl, result => matchTimeline = result));

                    if (!string.IsNullOrEmpty(matchTimeline))
                    {
                        // save matchTimeline to file named Timeline<currentMatchID>.txt 
                        debugLog("Saving timeline to Timeline" + currentMatchID + ".txt.");
                        File.WriteAllText(crawledDataFolder.text + "/Timeline" + currentMatchID + ".txt", matchTimeline);
                        matchesCrawled++;

                        if(matchesCrawled >= maxMatchesToCrawl)
                        {
                            debugLog("Reached max matches to crawl, exiting.");
                            Application.Quit();
                        }
                    }

                    if(!crawling)
                    {
                        yield break;
                    }
                }
            }
        }
    }

    private IEnumerator crawlMatch(string matchID)
    {
        string matchUrl = "https://na1.api.riotgames.com/lol/match/v3/matches/" + matchID + "?api_key=" + apiKey;
        string matchBody = "";
        yield return StartCoroutine(request(matchUrl, result => matchBody = result));

        if (!string.IsNullOrEmpty(matchBody))
        {
            // first save this match to file named Match<currentMatchID>.txt
            debugLog("Saving match to Match" + matchID + ".txt.");
            File.WriteAllText(crawledDataFolder.text + "/Match" + matchID + ".txt", matchBody);

            // parse this match to get a list of account IDs that participated in the match
            string[] delimiters = { "\"accountId\":" };
            string[] tokens = matchBody.Split(delimiters, System.StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < tokens.Length; i++)
            {
                string participantAccountID = tokens[i].Substring(0, tokens[i].IndexOf(','));

                if (!accounts.Contains(participantAccountID))
                {
                    accounts.Add(participantAccountID);
                    accountsToProcess.Enqueue(participantAccountID);
                }
            }
        }
    }

    private IEnumerator request(string url, System.Action<string> result)
    {
        requestsCompletedInTheLastMinute++;

        if (requestsCompletedInTheLastMinute > maxRequestsPerMinute)
        {
            float timeToWait = 60.0f - timeElapsed;
            debugLog("Reached limit, waiting " + timeToWait + " seconds.");
            debugLog("Crawled " + matchesCrawled + " matches so far.");
            yield return new WaitForSeconds(timeToWait);
        }

        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.isNetworkError || www.isHttpError)
        {
            debugLog("error downloading: " + www.error);
            result(null);
            yield break;
        }
        else
        {
            result(www.downloadHandler.text);
        }
    }

    private void debugLog(string text)
    {
        if(debugText.text.Length > 16000)
        {
            debugText.text = "";
        }
        debugText.text += text + "\n";
        Debug.Log(text);
    }
}
