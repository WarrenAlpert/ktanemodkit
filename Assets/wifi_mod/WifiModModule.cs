using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;
using System.Threading;
using System;
using System.Linq;

public class WifiModModule : MonoBehaviour
{
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

    const int NumRows = 4;
    const int NumColumns = 6;

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
    int attemptNumber = 0;

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
        attemptNumber++;

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
        this.dots[this.bomberPosition.R, this.bomberPosition.C].GetComponent<SpriteRenderer>().color = Color.white;

        this.dronePositions = new Dictionary<DroneName, Position>
                                {
                                    { DroneName.A, new Position{ R = 0, C = 0 } },
                                    { DroneName.B, new Position{ R = 0, C = 1 } },
                                    { DroneName.C, new Position{ R = 1, C = 0 } },
                                    { DroneName.D, new Position{ R = 1, C = 1 } },
                                };

        int row = random.Next(0, NumRows);

        bomberPosition = new Position
        {
            R = row,
            C = random.Next(row < 3 ? 3 : 0, NumColumns),
        };

        this.dots[this.bomberPosition.R, this.bomberPosition.C].GetComponent<SpriteRenderer>().color = Color.red;

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

    void Jam(JamType jamType)
    {

    }

    private void UpdateDotColor(Position position, Color color)
    {
        this.dots[position.R, position.C].GetComponent<SpriteRenderer>().color = color;
    }

    private void UpdateDotText(Position position, string text)
    {
        this.dots[position.R, position.C].GetComponentInChildren<TextMesh>().text = text;
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

            if (position.R == bomberPosition.R && position.C == bomberPosition.C)
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

                if (position.R == otherPosition.R && position.C == otherPosition.C)
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
                    R = fromPosition.R - 1,
                    C = fromPosition.C
                };
            case (Direction.Down):
                return new Position
                {
                    R = fromPosition.R + 1,
                    C = fromPosition.C
                };
            case (Direction.Left):
                return new Position
                {
                    R = fromPosition.R,
                    C = fromPosition.C - 1
                };
            case (Direction.Right):
                return new Position
                {
                    R = fromPosition.R,
                    C = fromPosition.C + 1
                };
            default:
                throw new ArgumentOutOfRangeException("Unexpected direction to move in.");
        }
    }

    private HashSet<Direction> GetAllowedMoves(Position fromPosition)
    {
        HashSet<Direction> allowedDirections = new HashSet<Direction>();

        if (GetMoveDestination(fromPosition, Direction.Up).R >= 0)
        {
            allowedDirections.Add(Direction.Up);
        }
        if (GetMoveDestination(fromPosition, Direction.Left).C >= 0)
        {
            allowedDirections.Add(Direction.Left);
        }
        if (GetMoveDestination(fromPosition, Direction.Down).R <= NumRows - 1)
        {
            allowedDirections.Add(Direction.Down);
        }
        if (GetMoveDestination(fromPosition, Direction.Right).C <= NumColumns - 1)
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

    // Stolen from: https://stackoverflow.com/questions/2049582/how-to-determine-if-a-point-is-in-a-2d-triangle
    private bool PointInTriangle(Position bomber, Position p0, Position p1, Position p2)
    {
        var s = p0.C * p2.R - p0.R * p2.C + (p2.C - p0.C) * bomber.R + (p0.R - p2.R) * bomber.C;
        var t = p0.R * p1.C - p0.C * p1.R + (p0.C - p1.C) * bomber.R + (p1.R - p0.R) * bomber.C;

        if ((s < 0) != (t < 0))
            return false;

        var A = -p1.C * p2.R + p0.C * (p2.R - p1.R) + p0.R * (p1.C - p2.C) + p1.R * p2.C;
        if (A < 0.0)
        {
            s = -s;
            t = -t;
            A = -A;
        }
        return s > 0 && t > 0 && (s + t) <= A;
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
                Dictionary<string, string> queryStrings = new Dictionary<string, string>();
                string query = request.Url.Query;
                if (query.Length > 1)
                {
                    // Leading '?'
                    query = query.Substring(1);
                }
                foreach (string fullQueryString in query.Split('&'))
                {
                    string[] split = fullQueryString.Split('=');
                    if (split.Length != 2)
                    {
                        continue;
                    }

                    queryStrings.Add(split[0], split[1]);
                }

                if (this.connectionTextColor != Color.green)
                {
                    this.connectionTextColor = Color.green;
                    actions.Enqueue(delegate () { this.connectionText.color = Color.green; });
                }

                DroneName selectedDrone = DroneName.A;
                string drone;
                if (queryStrings.TryGetValue("drone", out drone))
                {
                    drone = drone.ToUpperInvariant();
                    foreach (DroneName possibleDrone in Enum.GetValues(typeof(DroneName)))
                    {
                        if (possibleDrone.ToString() == drone)
                        {
                            selectedDrone = possibleDrone;
                            break;
                        }
                    }
                }

                string attemptNumString;
                string move = null;
                string jam = null;
                if (queryStrings.TryGetValue("attempt", out attemptNumString) && this.attemptNumber.ToString() == attemptNumString)
                {
                    if (queryStrings.TryGetValue("move", out move))
                    {
                        foreach (Direction possibleDirection in Enum.GetValues(typeof(Direction)))
                        {
                            if (string.Equals(move, possibleDirection.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                if (GetAllowedMoves(this.dronePositions[selectedDrone]).Contains(possibleDirection))
                                {
                                    actions.Enqueue(delegate () { ChangeDronePosition(selectedDrone, possibleDirection); });
                                }
                                break;
                            }
                        }
                    }
                    else if (queryStrings.TryGetValue("jam", out jam))
                    {
                        foreach (JamType possibleJamType in JamTypes.All())
                        {
                            if (string.Equals(jam, possibleJamType.DisplayName, StringComparison.OrdinalIgnoreCase))
                            {
                                actions.Enqueue(delegate () { Jam(possibleJamType); });
                                break;
                            }
                        }
                    }
                }

                responseString +=
                    selectedDrone.ToString() + " " +
                    (move != null && move.Count() > 0 ? move : "(no move)") + " " +
                    (jam != null && jam.Count() > 0 ? jam : "(no jam)");
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
        public int R;
        public int C;
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

        public static IEnumerable<JamType> All()
        {
            return new List<JamType> { TwoG, ThreeG, FourG };
        }
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
