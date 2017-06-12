using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;
using System.Threading;
using System;
using System.Linq;

public class WifiModModule : MonoBehaviour
{
    const int NumRows = 4;
    const int NumColumns = 6;

    // In seconds
    float StartingBomberMoveInterval = 10;
    float BomberMoveIntervalStrikePenalty = 3;
    float BombererMoveIntervalMin = 4;

    enum DroneName
    {
        A,
        B,
        C,
        D
    }

    Dictionary<JamType, List<DroneName>> DroneCapabilities = new Dictionary<JamType, List<DroneName>>
    {
        { JamTypes.TwoG , new List<DroneName> { DroneName.B, DroneName.C, DroneName.D } },
        { JamTypes.ThreeG , new List<DroneName> { DroneName.A, DroneName.C, DroneName.D } },
        { JamTypes.FourG , new List<DroneName> { DroneName.A, DroneName.B, DroneName.C } }
    };

    public static List<int> usedPorts = new List<int>();
    Transform droneMap;
    Transform[,] dots;
    string serialNumber;

    HashSet<char> vowels = new HashSet<char> { 'A', 'E', 'I', 'O', 'U' };
    HashSet<char> oddNums = new HashSet<char> { '1', '3', '5', '7', '9' };

    Dictionary<DroneName, Position> dronePositions;

    Position bomberPosition;

    TextMesh connectionText;
    Color connectionTextColor;
    string ipAndPort;
    int port;
    bool gameActive = false;
    float bomberMoveTimeRemaining;
    JamType correctJamType;
    bool bomberRunning;

    System.Random random;
    Thread workerThread;

    Queue<Action> actions;

    void Start()
    {
        Init();
    }

    void Init()
    {
        GetComponent<KMBombModule>().OnActivate += OnActivateCaller;

        KMGameplayRoom room = GetComponent<KMGameplayRoom>();
        if (room != null)
        {
            room.OnLightChange += OnLightChange;
        }
    }

    void OnActivateCaller()
    {
        gameActive = true;

        // (callOnLightChange) == (Running in test harness)
        OnReset(GetComponent<KMGameplayRoom>() == null);
    }

    void OnReset(bool callOnLightChange)
    {
        this.bomberRunning = true;
        bomberMoveTimeRemaining = StartingBomberMoveInterval;

        List<string> queryList = GetComponent<KMBombInfo>().QueryWidgets(KMBombInfo.QUERYKEY_GET_SERIAL_NUMBER, null);
        serialNumber = queryList.Count == 1 ? queryList[0].Replace("\"}", "").Split('"').Last() : "TZST12";

        if (this.oddNums.Contains(serialNumber[5]))
        {
            this.correctJamType = JamTypes.TwoG;
        }
        else
        {
            this.correctJamType = JamTypes.FourG;

            foreach (char c in serialNumber)
            {
                if (this.vowels.Contains(c))
                {
                    this.correctJamType = JamTypes.ThreeG;
                }
            }
        }

        this.connectionText.color = connectionTextColor;
        this.dots[this.bomberPosition.r, this.bomberPosition.c].GetComponent<SpriteRenderer>().color = Color.white;

        this.dronePositions = new Dictionary<DroneName, Position>
                                {
                                    { DroneName.A, new Position{ r = 0, c = 0 } },
                                    { DroneName.B, new Position{ r = 0, c = 1 } },
                                    { DroneName.C, new Position{ r = 1, c = 0 } },
                                    { DroneName.D, new Position{ r = 1, c = 1 } },
                                };

        int row = random.Next(0, NumRows);

        bomberPosition = new Position
        {
            r = row,
            c = random.Next(row < 3 ? 3 : 0, NumColumns),
        };

        this.dots[this.bomberPosition.r, this.bomberPosition.c].GetComponent<SpriteRenderer>().color = Color.red;

        if (callOnLightChange)
        {
            OnLightChange(true);
        }
    }

    void CauseStrike()
    {
        if (StartingBomberMoveInterval - BomberMoveIntervalStrikePenalty >= BombererMoveIntervalMin)
        {
            StartingBomberMoveInterval -= BomberMoveIntervalStrikePenalty;
        }

        OnLightChange(false);
        OnReset(true);

        GetComponent<KMBombModule>().HandleStrike();
    }



    public void OnLightChange(bool on)
    {
        this.connectionText.text = !on ? "" : ipAndPort;

        foreach (KeyValuePair<DroneName, Position> droneToPosition in this.dronePositions)
        {
            UpdateDotText(droneToPosition.Value, !on ? "" : droneToPosition.Key.ToString());
            UpdateDotColor(droneToPosition.Value, !on ? Color.white : Color.clear);
        }
    }

    void ChangeDronePosition(DroneName droneName, Direction direction)
    {
        UpdateDotColor(this.dronePositions[droneName], Color.white);
        UpdateDotText(this.dronePositions[droneName], "");
        this.dronePositions[droneName] = GetMoveDestination(this.dronePositions[droneName], direction);
        UpdateDotText(this.dronePositions[droneName], droneName.ToString());
        UpdateDotColor(this.dronePositions[droneName], Color.clear);

        EvaluateCollisions();

        this.bomberRunning = true;
    }

    private void UpdateDotColor(Position position, Color color)
    {
        this.dots[position.r, position.c].GetComponent<SpriteRenderer>().color = color;
    }

    private void UpdateDotText(Position position, string text)
    {
        this.dots[position.r, position.c].GetComponentInChildren<TextMesh>().text = text;
    }

    private void ChangeBomberPosition()
    {
        HashSet<Direction> possibleMoves = GetAllowedMoves(bomberPosition);

        UpdateDotColor(this.bomberPosition, Color.white);
        this.bomberPosition = GetMoveDestination(this.bomberPosition, possibleMoves.ElementAt(random.Next(0, possibleMoves.Count)));
        UpdateDotColor(this.bomberPosition, Color.red);

        EvaluateCollisions();
    }

    private void EvaluateCollisions()
    {
        // Inneficient in multiple ways I know... I'm rushing...
        foreach (DroneName droneName in Enum.GetValues(typeof(DroneName)))
        {
            Position position = this.dronePositions[droneName];

            if (position.r == bomberPosition.r && position.c == bomberPosition.c)
            {
                CauseStrike();
                return;
            }

            foreach (DroneName otherDroneName in Enum.GetValues(typeof(DroneName)))
            {
                if (droneName == otherDroneName)
                {
                    continue;
                }

                Position otherPosition = this.dronePositions[otherDroneName];

                if (position.r == otherPosition.r && position.c == otherPosition.c)
                {
                    CauseStrike();
                    return;
                }
            }
        }
    }

    private Position GetMoveDestination(Position fromPosition, Direction direction)
    {
        switch (direction)
        {
            case (Direction.Up):
                return new Position
                {
                    r = fromPosition.r - 1,
                    c = fromPosition.c
                };
            case (Direction.Down):
                return new Position
                {
                    r = fromPosition.r + 1,
                    c = fromPosition.c
                };
            case (Direction.Left):
                return new Position
                {
                    r = fromPosition.r,
                    c = fromPosition.c - 1
                };
            case (Direction.Right):
                return new Position
                {
                    r = fromPosition.r,
                    c = fromPosition.c + 1
                };
            default:
                throw new ArgumentOutOfRangeException("Unexpected direction to move in.");
        }
    }

    private HashSet<Direction> GetAllowedMoves(Position fromPosition)
    {
        HashSet<Direction> allowedDirections = new HashSet<Direction>();

        if (GetMoveDestination(fromPosition, Direction.Up).r >= 0)
        {
            allowedDirections.Add(Direction.Up);
        }
        if (GetMoveDestination(fromPosition, Direction.Left).c >= 0)
        {
            allowedDirections.Add(Direction.Left);
        }
        if (GetMoveDestination(fromPosition, Direction.Up).r <= NumRows - 1)
        {
            allowedDirections.Add(Direction.Down);
        }
        if (GetMoveDestination(fromPosition, Direction.Right).c <= NumColumns - 1)
        {
            allowedDirections.Add(Direction.Right);
        }

        return allowedDirections;
    }

    void Awake()
    {
        actions = new Queue<Action>();
        this.connectionTextColor = Color.yellow;

        do
        {
            this.random = new System.Random();
            port = random.Next(8050, 8100);
        }
        while (usedPorts.Contains(port));

        this.droneMap = this.transform.FindChild("Model").FindChild("DroneMap");
        this.dots = new Transform[NumRows, NumColumns];
        this.ipAndPort = Dns.GetHostAddresses(Dns.GetHostName()).Single(i => i.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString() + ":" + port.ToString();
        this.connectionText = this.transform.FindChild("Model").FindChild("ConnectionBackground").GetComponentInChildren<TextMesh>();

        for (int r = 0; r < NumRows; r++)
        {
            for (int c = 0; c < NumColumns; c++)
            {
                this.dots[r, c] = this.droneMap.FindChild((r + 1) + "," + (c + 1));
            }
        }
        
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

        if (this.gameActive && this.bomberRunning)
        {
            bomberMoveTimeRemaining -= Time.deltaTime;
            if (bomberMoveTimeRemaining <= 0)
            {
                bomberMoveTimeRemaining = StartingBomberMoveInterval;
                ChangeBomberPosition();
            }
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

            if (gameActive && !request.Url.OriginalString.Contains("favicon"))
            {
                if (this.connectionTextColor != Color.green)
                {
                    this.connectionTextColor = Color.green;
                    actions.Enqueue(delegate () { this.connectionText.color = Color.green; });
                }

                if (request.Url.OriginalString.Contains("/p"))
                {
                    actions.Enqueue(delegate () { GetComponent<KMBombModule>().HandlePass(); });
                }
                else if (request.Url.OriginalString.Contains("/s"))
                {
                    actions.Enqueue(delegate () { CauseStrike(); });
                }
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

    protected void OnBombExplodes()
    {
        gameActive = false;
    }

    protected void OnBombDefused()
    {
        gameActive = false;
    }

    struct Position
    {
        public int r;
        public int c;
    }

    enum Direction
    {
        Left,
        Right,
        Up,
        Down
    }

    public static class JamTypes
    {
        public static JamType TwoG = new JamType { DisplayName = "2G" };
        public static JamType ThreeG = new JamType { DisplayName = "3G" };
        public static JamType FourG = new JamType { DisplayName = "4G" };
    }

    public struct JamType
    {
        public string DisplayName;
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
