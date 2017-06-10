using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;
using System.Threading;
using System;

public class WifiModModule : MonoBehaviour
{
    public static List<int> usedPorts = new List<int>();
    public KMSelectable[] buttons;
    KMAudio.KMAudioRef audioRef;
    int correctIndex;

    void Start()
    {
        Init();
    }

    void Init()
    {
        correctIndex = UnityEngine.Random.Range(0, 4);
        GetComponent<KMBombModule>().OnActivate += OnActivate;
        GetComponent<KMSelectable>().OnCancel += OnCancel;
        GetComponent<KMSelectable>().OnLeft += OnLeft;
        GetComponent<KMSelectable>().OnLeft += OnRight;
        GetComponent<KMSelectable>().OnSelect += OnSelect;
        GetComponent<KMSelectable>().OnDeselect += OnDeselect;
        GetComponent<KMSelectable>().OnHighlight += OnHighlight;

        for (int i = 0; i < buttons.Length; i++)
        {
            string label = i == correctIndex ? port.ToString() : "B";

            TextMesh buttonText = buttons[i].GetComponentInChildren<TextMesh>();
            buttonText.text = label;
            int j = i;
            buttons[i].OnInteract += delegate () { Debug.Log("Press #" + j); OnPress(j == correctIndex); return false; };
            buttons[i].OnInteractEnded += OnRelease;
        }
    }

    private void OnDeselect()
    {
        Debug.Log("ExampleModule2 OnDeselect.");
    }

    private void OnLeft()
    {
        Debug.Log("ExampleModule2 OnLeft.");
    }

    private void OnRight()
    {
        Debug.Log("ExampleModule2 OnRight.");
    }

    private void OnSelect()
    {
        Debug.Log("ExampleModule2 OnSelect.");
    }

    private void OnHighlight()
    {
        Debug.Log("ExampleModule2 OnHighlight.");
    }

    void OnActivate()
    {
        foreach (string query in new List<string> { KMBombInfo.QUERYKEY_GET_BATTERIES, KMBombInfo.QUERYKEY_GET_INDICATOR, KMBombInfo.QUERYKEY_GET_PORTS, KMBombInfo.QUERYKEY_GET_SERIAL_NUMBER, "example"})
        {
            List<string> queryResponse = GetComponent<KMBombInfo>().QueryWidgets(query, null);

            if (queryResponse.Count > 0)
            {
                Debug.Log(queryResponse[0]);
            }
        }

        int batteryCount = 0;
        List<string> responses = GetComponent<KMBombInfo>().QueryWidgets(KMBombInfo.QUERYKEY_GET_BATTERIES, null);
        foreach (string response in responses)
        {
            Dictionary<string, int> responseDict = JsonConvert.DeserializeObject<Dictionary<string, int>>(response);
            batteryCount += responseDict["numbatteries"];
        }

        Debug.Log("Battery count: " + batteryCount);
    }

    bool OnCancel()
    {
        Debug.Log("ExampleModule2 cancel.");

        return true;
    }

    void ToggleSquare()
    {
        if (spriteRenderer.color == Color.white)
        {
            spriteRenderer.color = Color.clear;
        }
        else
        {
            spriteRenderer.color = Color.white;
        }
    }

    //On pressing button a looped sound will play
    void OnPress(bool correctButton)
    {
        Debug.Log("Pressed " + correctButton + " button");

        ToggleSquare();

        if (correctButton)
        {
            audioRef = GetComponent<KMAudio>().PlayGameSoundAtTransformWithRef(KMSoundOverride.SoundEffect.AlarmClockBeep, transform);
            GetComponent<KMBombModule>().HandlePass();
        }
        else
        {
            audioRef = GetComponent<KMAudio>().PlaySoundAtTransformWithRef("doublebeep125loop", transform);
        }
    }

    //On releasing a button a looped sound will stop
    void OnRelease()
    {
        Debug.Log("OnInteractEnded Released");
        if(audioRef != null && audioRef.StopSound != null)
        {
            audioRef.StopSound();
        }
    }

    KMBombInfo bombInfo;
    KMGameCommands gameCommands;
    string modules;
    string solvableModules;
    string solvedModules;
    string bombState;
    SpriteRenderer spriteRenderer;
    int port;

    Thread workerThread;

    Queue<Action> actions;

    void Awake()
    {
        actions = new Queue<Action>();
        bombInfo = GetComponent<KMBombInfo>();
        bombInfo.OnBombExploded += OnBombExplodes;
        bombInfo.OnBombSolved += OnBombDefused;
        gameCommands = GetComponent<KMGameCommands>();
        List<string> ss = bombInfo.GetModuleNames();
        Debug.Log(string.Join(",", ss.ToArray()));
        bombState = "NA";

        spriteRenderer = this.transform.FindChild("Model").FindChild("New Sprite").gameObject.GetComponent<SpriteRenderer>();
        
        do
        {
            port = UnityEngine.Random.Range(8050, 8099);
        }
        while (usedPorts.Contains(port));

        usedPorts.Add(port);
        Debug.Log("usedPorts adding: " + port + ", count: " + usedPorts.Count);

        // Create the thread object. This does not start the thread.
        Worker workerObject = new Worker(this, port);
        workerThread = new Thread(workerObject.DoWork);
        // Start the worker thread.
        workerThread.Start(this);
    }

    void Update()
    {
        if (actions.Count > 0)
        {
            Action action = actions.Dequeue();
            action();
        }
    }

    void OnDestroy()
    {
        workerThread.Abort();
    }

    // This example requires the System and System.Net namespaces.
    public void SimpleListenerExample(string[] prefixes)
    {
        // Create a listener.
        HttpListener listener = new HttpListener();
        // Add the prefixes.
        foreach (string s in prefixes)
        {
            listener.Prefixes.Add(s);
        }
        listener.Start();
        while (true)
        {
            // Note: The GetContext method blocks while waiting for a request. 
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            // Obtain a response object.
            HttpListenerResponse response = context.Response;
            // Construct a response.
            string responseString = "";

            if (request.Url.OriginalString.Contains("bombInfo"))
            {
                ToggleSquare();
                actions.Enqueue(delegate () { GetComponent<KMBombModule>().HandlePass(); });
                responseString = GetBombInfo();
            }

            if (request.Url.OriginalString.Contains("startMission"))
            {
                string missionId = request.QueryString.Get("missionId");
                string seed = request.QueryString.Get("seed");
                responseString = StartMission(missionId, seed);
            }

            if (request.Url.OriginalString.Contains("causeStrike"))
            {
                string reason = request.QueryString.Get("reason");
                responseString = CauseStrike(reason);
            }

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }
    }

    protected string StartMission(string missionId, string seed)
    {
        actions.Enqueue(delegate () { gameCommands.StartMission(missionId, seed); });

        return missionId + " " + seed;
    }

    protected string CauseStrike(string reason)
    {
        actions.Enqueue(delegate () { gameCommands.CauseStrike(reason); });

        return reason;
    }

    protected string GetBombInfo()
    {
        if (bombInfo.IsBombPresent())
        {
            if (bombState == "NA")
            {
                bombState = "Active";
            }
        }
        else if (bombState == "Active")
        {
            bombState = "NA";
        }

        string time = bombInfo.GetFormattedTime();
        int strikes = bombInfo.GetStrikes();
        modules = GetListAsHTML(bombInfo.GetModuleNames());
        solvableModules = GetListAsHTML(bombInfo.GetSolvableModuleNames());
        solvedModules = GetListAsHTML(bombInfo.GetSolvedModuleNames());

        string responseString = string.Format(
            "<HTML><BODY>"
            + "<span>Time: {0}</span><br>"
            + "<span>Strikes: {1}</span><br>"
            + "<span>Modules: {2}</span><br>"
            + "<span>Solvable Modules: {3}</span><br>"
            + "<span>Solved Modules: {4}</span><br>"
            + "<span>State: {5}</span><br>"
            + "</BODY></HTML>", time, strikes, modules, solvableModules, solvedModules, bombState);

        return responseString;
    }

    protected void OnBombExplodes()
    {
        bombState = "Exploded";
    }

    protected void OnBombDefused()
    {
        bombState = "Defused";
    }

    protected string GetListAsHTML(List<string> list)
    {
        string listString = "";

        foreach (string s in list)
        {
            listString += s + ", ";
        }

        return listString;
    }

    public class Worker
    {
        WifiModModule service;
        int port;

        public Worker(WifiModModule s, int port)
        {
            service = s;
            this.port = port;
        }

        // This method will be called when the thread is started. 
        public void DoWork()
        {
            service.SimpleListenerExample(new string[] { "http://*:" + port + "/" });
        }
    }
}
