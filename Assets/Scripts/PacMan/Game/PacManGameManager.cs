using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PacMan.Interface;
using PacMan.Interface.PacMan;
using PacMan.Local;
using PacMan.Network;
using PacMan.Network.Generated;
using Scripts.Map;
using UnityEngine;
using ProtoGameState = PacMan.Network.Generated.GameState;

namespace PacMan.Game
{
    public class PacManGameManager : MonoBehaviour
    {
        private const int ServerClientCount = TeamAssignmentUtil.ExpectedNetworkClientCount;

        public MapManager mapManager;
        public GameObject agentPrefab;
        public GameObject foodPrefab;
        public GameObject capsulePrefab;

        public int blueFood;
        public int redFood;
        public int redScore;
        public int blueScore;

        protected float StartTime;
        public float matchTime;
        public float matchLength = 240; //From original 3000 / 4

        public bool finished = false;
        public int agentsPerTeam;
        public string teamName = "";

        public List<PacManAgentManager> agents;
        public List<PacManAgentManager> redAgents;
        public List<PacManAgentManager> blueAgents;
        public List<GameObject> foodList;
        public List<GameObject> capsules;
        public bool started;

        private IPacManWorkerInterface _pacManWorker;
        public bool AutomaticRestart;

        public GameRecorder gameRecorder;
        private PacManManagerModeSelector _selector;
        private CommunicatorHttpServer serverReference;
        private bool _serverDisposedOnQuit;
        private bool _waitingForClientActions;
        private bool _configuredManualSimulation;
        private SimulationMode _previousSimulationMode;
        private bool _loggedServerOwnershipLayout;
        private readonly HashSet<string> _loggedOwnershipWarnings = new();

        protected ManagerMode CurrentMode => PacManManagerModeSelector.mode_override ?? (_selector != null ? _selector.mode : ManagerMode.Local);
        public ManagerMode ActiveMode => CurrentMode;
        public bool UsesManualSimulation => CurrentMode == ManagerMode.Server || CurrentMode == ManagerMode.Client;
        public float CurrentSimulationTime => matchTime;
        protected virtual bool ShouldDeferAgentAIInitialization => false;

        void Awake()
        {
            StartTime = Time.time;
            _pacManWorker = GetComponent<IPacManWorkerInterface>();
            _selector = FindFirstObjectByType<PacManManagerModeSelector>();
            ConfigureCommandLineRecording();
            ConfigurePhysicsSimulationMode();

            if (CurrentMode == ManagerMode.Server)
            {
                serverReference = CommunicatorHttpServer.Instance; //Lazy init the server here
            }
        }

        public virtual void Start()
        {
            if (LoadMap())
            {
                return;
            }

            Initialize();
        }

        protected bool LoadMap()
        {
            if (string.IsNullOrWhiteSpace(PacManManagerModeSelector.map_override))
            {
                return false;
            }

            StartCoroutine(ApplyMapSelectionAndInitialize(PacManManagerModeSelector.map_override));
            return true;
        }

        protected IEnumerator ApplyMapSelectionAndInitialize(string requestedMapName)
        {
            yield return ApplyMapSelectionAsync(requestedMapName);
            Initialize();
        }

        protected IEnumerator ApplyMapSelectionAsync(string requestedMapName)
        {
            var resolvedMapName = requestedMapName?.Trim();
            if (string.IsNullOrWhiteSpace(resolvedMapName) || mapManager == null)
            {
                yield break;
            }

            // Keep the map manager's selected file aligned with the loaded scene map so
            // initialization and outbound server state reference the same map.
            mapManager.fileName = resolvedMapName;
            mapManager.ClearMap();

            // MapManager clears children with Destroy() in play mode, so wait a frame
            // before loading the replacement map to avoid mixing old and new spawn data.
            if (Application.isPlaying)
            {
                yield return null;
            }

            mapManager.LoadMap(resolvedMapName);
        }

        protected void Initialize()
        {
            mapManager.Initialize();
            RestartGame();

            if (CurrentMode == ManagerMode.Server)
            {
                BeginServerActionWait();
            }
        }

        public void RestartGame()
        {
            foodList.ForEach(food => _pacManWorker.RemoveObject(food));
            capsules.ForEach(capsule => _pacManWorker.RemoveObject(capsule));
            StartGame();
            SynchronizeAgentServerIndices();
            agents.ForEach(agent => agent.Initialize(this, ShouldDeferAgentAIInitialization));
            LogServerOwnershipLayout();
            gameRecorder?.StartRecording(this);
        }

        public void StartGame()
        {
            StartTime = Time.time;
            matchTime = 0f;
            _waitingForClientActions = false;
            _loggedServerOwnershipLayout = false;
            _loggedOwnershipWarnings.Clear();
            if (CurrentMode == ManagerMode.Server)
            {
                serverReference?.ResetRequestMetrics();
            }

            if (agents.Count == mapManager.startPositions.Count) //TODO: Variable agent counts map to map?
            {
                agents.ForEach(agent => _pacManWorker.ResetAgent(agent.gameObject));
            }

            agentsPerTeam = mapManager.startPositions.Count / 2;
            if (agents == null || agents.Count == 0)
            {
                agents = new List<PacManAgentManager>();
                AddTeam("Blue");
                AddTeam("Red");
            }

            CreateEdibles();
            started = true;
            finished = false;
        }

        public void FixedUpdate()
        {
            if (UsesManualSimulation)
            {
                return;
            }

            if (started)
            {
                if (!finished)
                {
                    HandleAgentsLocal();
                    AdvanceSimulationClock(Time.fixedDeltaTime);
                    RefreshDerivedState();
                    gameRecorder?.AppendState(this);
                }
            }

            if (finished && AutomaticRestart)
            {
                RestartGame();
            }
        }

        public void Update()
        {
            if (!UsesManualSimulation || CurrentMode != ManagerMode.Server)
            {
                return;
            }

            if (!started)
            {
                return;
            }

            if (finished)
            {
                if (AutomaticRestart)
                {
                    RestartGame();
                }

                return;
            }

            HandleAgentsServerManual();

            if (finished && AutomaticRestart)
            {
                RestartGame();
            }
        }

        private void HandleAgentsLocal()
        {
            agents.ForEach(agent => agent.UpdateAction());
            agents.ForEach(agent => agent.UpdateObservations());
        }

        private void HandleAgentsServerManual()
        {
            if (!_waitingForClientActions)
            {
                BeginServerActionWait();
                return;
            }

            if (!serverReference.TryFetchActionPerClient(out var actionsByClient))
            {
                return;
            }

            LogServerOwnershipLayout();
            _waitingForClientActions = false;
            for (var i = 0; i < actionsByClient.Count; i++)
            {
                var controlledAgentIndices = GetControlledAgentIndicesForClientSlot(i);
                if (controlledAgentIndices.Count == 0)
                {
                    LogOwnershipWarningOnce($"missing-owner:{i}", $"Client slot {i} does not own any agents. Ownership layout: {GetClientOwnershipDescription()}");
                    continue;
                }

                var actionsForClient = actionsByClient[i];
                if (actionsForClient.Count != controlledAgentIndices.Count)
                {
                    LogOwnershipWarningOnce($"action-count:{i}", $"Client slot {i} submitted {actionsForClient.Count} actions for {controlledAgentIndices.Count} owned agents. Missing agents will keep a neutral action.");
                }

                var updatedAgentIndices = new HashSet<int>();

                for (var j = 0; j < actionsForClient.Count; j++)
                {
                    var action = actionsForClient[j];
                    var agentIndex = action.Index;
                    if (agentIndex < 0 || agentIndex >= agents.Count || !controlledAgentIndices.Contains(agentIndex))
                    {
                        if (j >= controlledAgentIndices.Count)
                        {
                            LogOwnershipWarningOnce($"extra-action:{i}", $"Client slot {i} submitted extra actions beyond its owned agents. Ownership layout: {GetClientOwnershipDescription()}");
                            continue;
                        }

                        if (agentIndex >= 0 && agentIndex < agents.Count)
                        {
                            LogOwnershipWarningOnce($"invalid-owner:{i}", $"Client slot {i} attempted to drive agent {agentIndex}, which is outside its ownership. Falling back to owned agents {string.Join(",", controlledAgentIndices)}.");
                        }

                        agentIndex = controlledAgentIndices[j];
                    }

                    if (!updatedAgentIndices.Add(agentIndex))
                    {
                        LogOwnershipWarningOnce($"duplicate-action:{i}", $"Client slot {i} submitted duplicate actions for agent {agentIndex}. Later duplicates are ignored.");
                        continue;
                    }

                    agents[agentIndex].UpdateAction(action);
                }

                foreach (var agentIndex in controlledAgentIndices)
                {
                    if (updatedAgentIndices.Contains(agentIndex))
                    {
                        continue;
                    }

                    agents[agentIndex].UpdateAction(new PacManAction
                    {
                        Index = agentIndex
                    });
                }
            }

            AdvanceSimulationClock(Time.fixedDeltaTime);
            Physics.Simulate(Time.fixedDeltaTime);
            agents.ForEach(agent => agent.CompleteSimulationStep());
            Physics.SyncTransforms();
            RefreshDerivedState();
            gameRecorder?.AppendState(this);

            if (!finished)
            {
                BeginServerActionWait();
            }
        }

        private void BeginServerActionWait()
        {
            agents.ForEach(agent => agent.UpdateObservations());
            SendState();
            serverReference?.StartStepRequestWaitCycle();
            _waitingForClientActions = true;
        }

        private void SendState()
        {
            serverReference.StepCompleted(BuildGameStatesForClients());
        }

        protected void RefreshDerivedState()
        {
            blueFood = foodList.FindAll(food => TeamAssignmentUtil.CheckTeam(food) == Team.Blue && food.activeSelf).Count;
            redFood = foodList.FindAll(food => TeamAssignmentUtil.CheckTeam(food) == Team.Red && food.activeSelf).Count;
            blueScore = blueFood - redFood;
            redScore = redFood - blueFood;
            finished = matchTime > matchLength;
        }

        protected void AdvanceSimulationClock(float deltaTime)
        {
            matchTime += deltaTime;
        }

        private List<ProtoGameState> BuildGameStatesForClients()
        {
            var gameStates = new List<ProtoGameState>(ServerClientCount);
            for (var i = 0; i < ServerClientCount; i++)
            {
                gameStates.Add(GameStateParser.WriteStateForClient(this, i, ServerClientCount));
            }

            return gameStates;
        }

        private List<int> GetControlledAgentIndicesForClientSlot(int clientSlot)
        {
            return TeamAssignmentUtil.GetControlledAgentIndicesForClientSlot(GetAgentTags(), clientSlot, ServerClientCount);
        }

        private List<string> GetAgentTags()
        {
            return agents?.Select(agent => agent != null ? agent.tag : string.Empty).ToList() ?? new List<string>();
        }

        private string GetClientOwnershipDescription()
        {
            return TeamAssignmentUtil.DescribeNetworkOwnership(GetAgentTags(), ServerClientCount);
        }

        private void LogServerOwnershipLayout()
        {
            if (_loggedServerOwnershipLayout || CurrentMode != ManagerMode.Server || agents == null || agents.Count == 0)
            {
                return;
            }

            Debug.Log($"Server ownership layout: {GetClientOwnershipDescription()}");
            if (!TeamAssignmentUtil.HasBalancedNetworkTeams(GetAgentTags(), ServerClientCount))
            {
                LogOwnershipWarningOnce("unbalanced-layout", $"Detected an uneven network team layout. Current ownership: {GetClientOwnershipDescription()}");
            }

            _loggedServerOwnershipLayout = true;
        }

        private void LogOwnershipWarningOnce(string warningKey, string message)
        {
            if (_loggedOwnershipWarnings.Add(warningKey))
            {
                Debug.LogWarning(message);
            }
        }

        protected void SynchronizeAgentServerIndices()
        {
            if (agents == null)
            {
                return;
            }

            for (var i = 0; i < agents.Count; i++)
            {
                if (agents[i] != null)
                {
                    agents[i].serverIndex = i;
                }
            }
        }

        public virtual string GetDisplayTeamName()
        {
            return ResolveDisplayTeamName(teamName, string.Empty);
        }

        public virtual IReadOnlyList<NetworkRequestDisplayEntry> GetNetworkRequestDisplayEntries()
        {
            if (CurrentMode != ManagerMode.Server || serverReference == null)
            {
                return Array.Empty<NetworkRequestDisplayEntry>();
            }

            return serverReference.GetRequestDisplayEntries();
        }

        protected static string ResolveDisplayTeamName(string configuredTeamName, string fallbackTeamTag)
        {
            if (!string.IsNullOrWhiteSpace(configuredTeamName))
            {
                return configuredTeamName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fallbackTeamTag))
            {
                return $"{fallbackTeamTag} Team";
            }

            return "Pending";
        }


        public void AddTeam(string team_tag)
        {
            int index = team_tag == "Blue" ? 1 : 2;
            for (int i = 0; i < agentsPerTeam; i++)
            {
                var position = mapManager.startPositions[i + (index - 1) * agentsPerTeam];
                var agent = _pacManWorker.CreateAgent(agentPrefab, gameObject, mapManager.transform.Find("Starts").transform.position + position, team_tag);

                agents.Add(agent.GetComponent<PacManAgentManager>());
                if (team_tag == "Blue")
                {
                    blueAgents.Add(agent.GetComponent<PacManAgentManager>());
                }
                else
                {
                    redAgents.Add(agent.GetComponent<PacManAgentManager>());
                }
            }
        }

        private void CreateEdibles()
        {
            foodList = mapManager.targetPositions.Select(pos => _pacManWorker.CreateEdible(gameObject, foodPrefab, mapManager.transform.Find("Targets").position + pos)).ToList();
            var capsulesObject = mapManager.transform.Find("Capsules");
            capsules = new List<GameObject>();
            foreach (Transform transform in capsulesObject)
            {
                capsules.Add(_pacManWorker.CreateEdible(gameObject, capsulePrefab, capsulesObject.position + transform.localPosition));
            }
        }

        public void DropFood(PacManAgentManager pacManAgentAgent, bool success)
        {
            if (pacManAgentAgent.GetCarriedFoodCount() > 0)
            {
                Func<float, bool> predicate = null;
                if (TeamAssignmentUtil.CheckTeam(pacManAgentAgent.gameObject) == Team.Red && success)
                    predicate = x => x >= 0;
                if (TeamAssignmentUtil.CheckTeam(pacManAgentAgent.gameObject) == Team.Blue && success)
                    predicate = x => x < 0;
                if (TeamAssignmentUtil.CheckTeam(pacManAgentAgent.gameObject) == Team.Red && !success)
                    predicate = x => x < 0;
                if (TeamAssignmentUtil.CheckTeam(pacManAgentAgent.gameObject) == Team.Blue && !success)
                    predicate = x => x >= 0;

                var foodMap = BuildMapWithFood();

                var worldToCell = foodMap.WorldToCell(pacManAgentAgent.gameObject.transform.position);
                var agentPos = new Vector2Int(worldToCell.x, worldToCell.z);
                var nearestFreeCells = foodMap.traversabilityPerCell.ToList()
                    .FindAll(pair => pair.Value == ObstacleMap.Traversability.Free && predicate.Invoke(pair.Key.x)) //TODO: Does not filter food or capsules in the way
                    .Select(pair => pair.Key)
                    .Select(cell => (cell, cell - agentPos))
                    .OrderBy(tuple => Mathf.Abs(tuple.Item2.x) + Mathf.Abs(tuple.Item2.y))
                    .Select(tuple => tuple.cell)
                    .Take(pacManAgentAgent.GetCarriedFoodCount())
                    .Select(vec => foodMap.CellToWorld(new Vector3Int(vec.x, 0, vec.y)) + foodMap.trueScale / 2) //TODO: Needs checking vec => _grid.CellToWorld(new Vector3Int(vec.x, vec.y, 0)) + new Vector3(_grid.cellSize.x / 2, 0.4f, _grid.cellSize.y / 2)
                    .ToList();

                for (int i = 0; i < nearestFreeCells.Count; i++)
                {
                    pacManAgentAgent.foodCarried[i].transform.position = nearestFreeCells[i];
                    pacManAgentAgent.foodCarried[i].SetActive(true);
                }

                pacManAgentAgent.foodCarried.Clear();
            }
        }

        private ObstacleMap BuildMapWithFood()
        {
            var tempObst = new List<GameObject>();
            tempObst.AddRange(foodList);
            tempObst.AddRange(capsules);

            var obstacleObjects = mapManager.GetObstacleObjects();
            obstacleObjects.AddRange(tempObst);

            var obstacleMap = new ObstacleMap(mapManager, obstacleObjects, Vector3.one);
            obstacleMap.margin = new Vector3(0.95f, 0.1f, 0.95f);
            obstacleMap.layerNames = new List<string> { "Obstacle", "Food" };
            obstacleMap.GenerateMap();
            return obstacleMap;
        }

        public void EatFood(GameObject gameObject)
        {
            if (foodList.Contains(gameObject))
            {
                gameObject.gameObject.SetActive(false);
            }
        }

        public void EatCapsule(PacManAgentManager pacManAgentAgent, GameObject gameObject)
        {
            if (capsules.Contains(gameObject))
            {
                var scaredTeam = pacManAgentAgent.CompareTag("Red") ? blueAgents : redAgents;

                scaredTeam.ForEach(agent =>
                {
                    var otherAgent = agent.GetComponent<PacManAgentManager>();
                    otherAgent.SetScared(true, CurrentSimulationTime + 10f);
                });

                capsules.Remove(gameObject);
                _pacManWorker.RemoveObject(gameObject);
            }
        }

        private void ConfigurePhysicsSimulationMode()
        {
            if (!UsesManualSimulation)
            {
                return;
            }

            _previousSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _configuredManualSimulation = true;
        }

        private void ConfigureCommandLineRecording()
        {
            if (CurrentMode == ManagerMode.Client || string.IsNullOrWhiteSpace(NetworkCommandLine.RecordingBaseName))
            {
                return;
            }

            gameRecorder ??= GetComponent<GameRecorder>();
            if (gameRecorder == null)
            {
                Debug.LogWarning("Recording was requested via -recording, but no GameRecorder component is attached to the active game manager. Adding");
                gameRecorder = gameObject.AddComponent<GameRecorder>();
                return;
            }

            gameRecorder.filename = NetworkCommandLine.RecordingBaseName;
            gameRecorder.enabled = true;
        }

        protected void RestorePhysicsSimulationMode()
        {
            if (!_configuredManualSimulation)
            {
                return;
            }

            Physics.simulationMode = _previousSimulationMode;
            _configuredManualSimulation = false;
        }

        private void OnApplicationQuit()
        {
            DisposeServerOnQuit();
        }

        protected void DisposeServerOnQuit()
        {
            if (_serverDisposedOnQuit)
            {
                return;
            }

            _serverDisposedOnQuit = true;

            if (!CommunicatorHttpServer.IsInitialized)
            {
                serverReference = null;
                return;
            }

            try
            {
                CommunicatorHttpServer.Instance.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to dispose HTTP server during application quit: {e}");
            }
            finally
            {
                serverReference = null;
            }
        }

        private void OnDestroy()
        {
            RestorePhysicsSimulationMode();
        }
    }
}
