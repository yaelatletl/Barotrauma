﻿#if CLIENT
using Barotrauma.Particles;
#endif
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class LevelObjectManager
    {
        const int GridSize = 2000;

        private List<LevelObjectPrefab> prefabs = new List<LevelObjectPrefab>();

        private List<LevelObject> objects;
        private List<LevelObject>[,] objectGrid;
        
        public LevelObjectManager(string configPath)
        {
            LoadConfig(configPath);
        }
        public LevelObjectManager(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                LoadConfig(file);
            }
        }
        private void LoadConfig(string configPath)
        {
            try
            {
                XDocument doc = XMLExtensions.TryLoadXml(configPath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    prefabs.Add(new LevelObjectPrefab(element));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(String.Format("Failed to load LevelObject prefabs from {0}", configPath), e);
            }
        }

        class SpawnPosition
        {
            public readonly GraphEdge GraphEdge;
            public readonly Vector2 Normal;
            public readonly LevelObjectPrefab.SpawnPosType SpawnPosType;
            public readonly Alignment Alignment;
            public readonly float Length;

            public SpawnPosition(GraphEdge graphEdge, Vector2 normal, LevelObjectPrefab.SpawnPosType spawnPosType, Alignment alignment)
            {
                GraphEdge = graphEdge;
                Normal = normal;
                SpawnPosType = spawnPosType;
                Alignment = alignment;
                
                Length = Vector2.Distance(graphEdge.Point1, graphEdge.Point2);
            }

            public float GetSpawnProbability(LevelObjectPrefab prefab)
            {
                if (prefab.ClusteringAmount <= 0.0f) return Length;

                float noise = (float)(
                    PerlinNoise.Perlin(GraphEdge.Point1.X / 10000.0f, GraphEdge.Point1.Y / 10000.0f, prefab.ClusteringGroup) +
                    PerlinNoise.Perlin(GraphEdge.Point1.X / 20000.0f, GraphEdge.Point1.Y / 20000.0f, prefab.ClusteringGroup));

                return Length * (float)Math.Pow(noise, prefab.ClusteringAmount);
            }
        }

        public void PlaceObjects(Level level, int amount)
        {
            objectGrid = new List<LevelObject>[
                (int)Math.Ceiling(level.Size.X / GridSize),
                (int)Math.Ceiling((level.Size.Y - level.BottomPos) / GridSize)];
            
            List<SpawnPosition> availableSpawnPositions = new List<SpawnPosition>();
            var levelCells = level.GetAllCells();
            availableSpawnPositions.AddRange(GetAvailableSpawnPositions(levelCells, LevelObjectPrefab.SpawnPosType.Wall));

            foreach (var extraWall in level.ExtraWalls)
            {
                availableSpawnPositions.AddRange(GetAvailableSpawnPositions(extraWall.Cells, LevelObjectPrefab.SpawnPosType.SeaFloor));
            }

            foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
            {
                foreach (var ruinShape in ruin.RuinShapes)
                {
                    foreach (var wall in ruinShape.Walls)
                    {
                        availableSpawnPositions.Add(new SpawnPosition(
                            new GraphEdge(wall.A, wall.B),
                            (wall.A + wall.B) / 2.0f - ruinShape.Center,
                            LevelObjectPrefab.SpawnPosType.RuinWall,
                            ruinShape.GetLineAlignment(wall)));
                    }
                }            
            }
                        
            objects = new List<LevelObject>();
            for (int i = 0; i < amount; i++)
            {
                //get a random prefab and find a place to spawn it
                LevelObjectPrefab prefab = GetRandomPrefab(level.GenerationParams.Name);
                SpawnPosition spawnPosition = FindObjectPosition(availableSpawnPositions, level, prefab);

                if (spawnPosition == null && prefab.SpawnPos != LevelObjectPrefab.SpawnPosType.None) continue;

                float rotation = 0.0f;
                if (prefab.AlignWithSurface && spawnPosition != null)
                {
                    rotation = MathUtils.VectorToAngle(new Vector2(spawnPosition.Normal.Y, spawnPosition.Normal.X));
                }
                rotation += Rand.Range(prefab.RandomRotation.X, prefab.RandomRotation.Y, Rand.RandSync.Server);

                Vector2 position = Vector2.Zero;
                Vector2 edgeDir = Vector2.UnitX;
                if (spawnPosition == null)
                {
                    position = new Vector2(
                        Rand.Range(0.0f, level.Size.X, Rand.RandSync.Server),
                        Rand.Range(0.0f, level.Size.Y, Rand.RandSync.Server));
                }
                else
                {
                    edgeDir = (spawnPosition.GraphEdge.Point1 - spawnPosition.GraphEdge.Point2) / spawnPosition.Length;
                    position = spawnPosition.GraphEdge.Point2 + edgeDir * Rand.Range(prefab.MinSurfaceWidth / 2.0f, spawnPosition.Length - prefab.MinSurfaceWidth / 2.0f, Rand.RandSync.Server);
                }

                var newObject = new LevelObject(prefab,
                    new Vector3(position, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, Rand.RandSync.Server)), Rand.Range(prefab.Scale.X, prefab.Scale.Y, Rand.RandSync.Server), rotation);
                AddObject(newObject, level);

                foreach (LevelObjectPrefab.ChildObject child in prefab.ChildObjects)
                {
                    int childCount = Rand.Range(child.MinCount, child.MaxCount, Rand.RandSync.Server);
                    for (int j = 0; j < childCount; j++)
                    {
                        var matchingPrefabs = prefabs.Where(p => child.AllowedNames.Contains(p.Name));
                        int prefabCount = matchingPrefabs.Count();
                        var childPrefab = prefabCount == 0 ? null : matchingPrefabs.ElementAt(Rand.Range(0, prefabCount, Rand.RandSync.Server));
                        if (childPrefab == null) continue;

                        Vector2 childPos = position + edgeDir * Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server) * prefab.MinSurfaceWidth;

                        var childObject = new LevelObject(childPrefab,
                            new Vector3(childPos, Rand.Range(childPrefab.DepthRange.X, childPrefab.DepthRange.Y, Rand.RandSync.Server)),
                            Rand.Range(childPrefab.Scale.X, childPrefab.Scale.Y, Rand.RandSync.Server),
                            rotation + Rand.Range(childPrefab.RandomRotation.X, childPrefab.RandomRotation.Y, Rand.RandSync.Server));

                        AddObject(childObject, level);
                    }
                }

            }
                
        }

        private void AddObject(LevelObject newObject, Level level)
        {
            foreach (LevelTrigger trigger in newObject.Triggers)
            {
                trigger.OnTriggered += (levelTrigger, obj) =>
                {
                    OnObjectTriggered(newObject, levelTrigger, obj);
                };
            }

            //calculate the positions of the corners of the rotated sprite
            Vector2 halfSize = newObject.Prefab.Sprite.size * newObject.Scale / 2;
            var spriteCorners = new List<Vector2>
            {
                -halfSize, new Vector2(-halfSize.X, halfSize.Y),
                halfSize, new Vector2(halfSize.X, -halfSize.Y)
            };

            Vector2 pivotOffset = newObject.Prefab.Sprite.Origin * newObject.Scale - halfSize;
            pivotOffset.X = -pivotOffset.X;
            pivotOffset = new Vector2(
                (float)(pivotOffset.X * Math.Cos(-newObject.Rotation) - pivotOffset.Y * Math.Sin(-newObject.Rotation)),
                (float)(pivotOffset.X * Math.Sin(-newObject.Rotation) + pivotOffset.Y * Math.Cos(-newObject.Rotation)));

            for (int j = 0; j < 4; j++)
            {
                spriteCorners[j] = new Vector2(
                    (float)(spriteCorners[j].X * Math.Cos(-newObject.Rotation) - spriteCorners[j].Y * Math.Sin(-newObject.Rotation)),
                    (float)(spriteCorners[j].X * Math.Sin(-newObject.Rotation) + spriteCorners[j].Y * Math.Cos(-newObject.Rotation)));

                spriteCorners[j] += new Vector2(newObject.Position.X, newObject.Position.Y) + pivotOffset;
            }

            float minX = spriteCorners.Min(c => c.X) - newObject.Position.Z;
            float maxX = spriteCorners.Max(c => c.X) + newObject.Position.Z;

            float minY = spriteCorners.Min(c => c.Y) - newObject.Position.Z - level.BottomPos;
            float maxY = spriteCorners.Max(c => c.Y) + newObject.Position.Z - level.BottomPos;

#if CLIENT
            if (newObject.ParticleEmitters != null)
            {
                foreach (ParticleEmitter emitter in newObject.ParticleEmitters)
                {
                    Rectangle particleBounds = emitter.CalculateParticleBounds(new Vector2(newObject.Position.X, newObject.Position.Y));
                    minX = Math.Min(minX, particleBounds.X);
                    maxX = Math.Max(maxX, particleBounds.Right);
                    minY = Math.Min(minY, particleBounds.Y - level.BottomPos);
                    maxY = Math.Max(maxY, particleBounds.Bottom - level.BottomPos);
                }
            }
#endif

            objects.Add(newObject);

            int xStart = (int)Math.Floor(minX / GridSize);
            int xEnd = (int)Math.Floor(maxX / GridSize);
            if (xEnd < 0 || xStart >= objectGrid.GetLength(0)) return;

            int yStart = (int)Math.Floor(minY / GridSize);
            int yEnd = (int)Math.Floor(maxY / GridSize);
            if (yEnd < 0 || yStart >= objectGrid.GetLength(1)) return;

            xStart = Math.Max(xStart, 0);
            xEnd = Math.Min(xEnd, objectGrid.GetLength(0) - 1);
            yStart = Math.Max(yStart, 0);
            yEnd = Math.Min(yEnd, objectGrid.GetLength(1) - 1);

            for (int x = xStart; x <= xEnd; x++)
            {
                for (int y = yStart; y <= yEnd; y++)
                {
                    if (objectGrid[x, y] == null) objectGrid[x, y] = new List<LevelObject>();
                    objectGrid[x, y].Add(newObject);
                }
            }            
        }

        private List<SpawnPosition> GetAvailableSpawnPositions(IEnumerable<VoronoiCell> cells, LevelObjectPrefab.SpawnPosType spawnPosType)
        {
            List<SpawnPosition> availableSpawnPositions = new List<SpawnPosition>();
            foreach (var cell in cells)
            {
                foreach (var edge in cell.edges)
                {
                    if (!edge.IsSolid || edge.OutsideLevel) continue;
                    Vector2 normal = edge.GetNormal(cell);

                    Alignment edgeAlignment = 0;
                    if (normal.Y < -0.5f) edgeAlignment |= Alignment.Bottom;
                    if (normal.Y > 0.5f) edgeAlignment |= Alignment.Top;
                    if (normal.X < -0.5f) edgeAlignment |= Alignment.Left;
                    if (normal.X > 0.5f) edgeAlignment |= Alignment.Right;

                    availableSpawnPositions.Add(new SpawnPosition(edge, normal, spawnPosType, edgeAlignment));
                }
            }
            return availableSpawnPositions;
        }

        private SpawnPosition FindObjectPosition(List<SpawnPosition> availableSpawnPositions, Level level, LevelObjectPrefab prefab)
        {
            if (prefab.SpawnPos == LevelObjectPrefab.SpawnPosType.None) return null;

            var suitableSpawnPositions = availableSpawnPositions.Where(sp => 
                prefab.SpawnPos.HasFlag(sp.SpawnPosType) && sp.Length >= prefab.MinSurfaceWidth).ToList();

            return ToolBox.SelectWeightedRandom(suitableSpawnPositions, suitableSpawnPositions.Select(sp => sp.GetSpawnProbability(prefab)).ToList(), Rand.RandSync.Server);
        }
        
        public void Update(float deltaTime)
        {
            foreach (LevelObject obj in objects)
            {
                obj.ActivePrefab = obj.Prefab;
                for (int i = 0; i < obj.Triggers.Count; i++)
                {
                    obj.Triggers[i].Update(deltaTime);
                    if (obj.Triggers[i].IsTriggered && obj.Prefab.OverrideProperties[i] != null)
                    {
                        obj.ActivePrefab = obj.Prefab.OverrideProperties[i];
                    }
                }

                if (obj.ActivePrefab.SonarDisruption > 0.0f)
                {
                    Level.Loaded?.SetSonarDisruptionStrength(new Vector2(obj.Position.X, obj.Position.Y), obj.ActivePrefab.SonarDisruption);
                }
            }

            UpdateProjSpecific(deltaTime);            
        }

        partial void UpdateProjSpecific(float deltaTime);

        private void OnObjectTriggered(LevelObject triggeredObject, LevelTrigger trigger, Entity triggerer)
        {
            if (trigger.TriggerOthersDistance <= 0.0f) return;
            foreach (LevelObject obj in objects)
            {
                if (obj == triggeredObject) continue;
                foreach (LevelTrigger otherTrigger in obj.Triggers)
                {
                    otherTrigger.OtherTriggered(triggeredObject, trigger);
                }
            }
        }

        private LevelObjectPrefab GetRandomPrefab(string levelType)
        {
            return ToolBox.SelectWeightedRandom(prefabs, prefabs.Select(p => p.GetCommonness(levelType)).ToList(), Rand.RandSync.Server);
        }
    }
}