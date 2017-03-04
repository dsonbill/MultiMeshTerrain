// Fast Hydraulic Erosion Simulation and Visualization on GP


// Development Credits

// Xing Mei  
// CASIA-LIAMA/NLPR, Beijing, China  
// xmei@(NOSPAM) nlpr.ia.ac.cn  

// Philippe Decaudin
// INRIA-Evasion, Grenoble, France
// & CASIA-LIAMA, Beijing, China
// philippe.decaudin@(NOSPAM) imag.fr

// Bao-Gang Hu
// CASIA-LIAMA/NLPR, Beijing, China
// hubg@(NOSPAM) nlpr.ia.ac.cn

// Original Paper
// http://www-ljk.imag.fr/Publications/Basilic/com.lmc.publi.PUBLI_Inproceedings@117681e94b6_fff75c/FastErosion_PG07.pdf


// Written for Unity by William C. Donaldson

// The MIT License (MIT)

// Copyright(c) 2017 William C. Donaldson
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using UnityEngine;

public class HydraulicErosion : MonoBehaviour
{

    public ErosionCell[,] grid;
    public int size = 256;
    public float timeMultiplier;
    public float pipeLength = 0.5f;
    public bool drawGizmos = false;
    public float perlinRain = 0.1f;
    public float perlinMultiplier = 4;
    public float perlinSeed = 0;
    public float seedIncrement = 0.02f;
    public float sedimentCapacityConstant = 0.1f;
    public float sedimentDissolvingConstant = 0.1f;
    public float sedimentDepositionConstant = 0.1f;
    public float waterEvaporationConstant = 0.1f;
    public float minimumTransportCapacity = 0.01f;
    public float perlinSeedPosition { get { return perlinSeed + size + perlinMovingPosition; } }

    public int currentX = 0;
    public int currentY = 0;
    public float perlinMovingPosition = 0;
    public CellUpdateMode currentMode = CellUpdateMode.Increment;

    public ProceduralTerrain terrain;

    public enum FluxMode
    {
        UpFlux,
        DownFlux,
        LeftFlux,
        RightFlux
    }

    public enum CellUpdateMode
    {
        Increment,
        Fluxes,
        Field,
        Erode,
        Transport,
        Evaporate,
        Render
    }

    void Start()
    {
        grid = new ErosionCell[size, size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                grid[x, y] = new ErosionCell();
                grid[x, y].terrainHeight = GetPerlinSample(x, y) * 100;
            }
        }
    }

    void Update()
    {
        if (!terrain.generationDone) return;
        for (int x = 0; x < size; x++)
        {
            if (grid[x, currentY] != null)
            {
                Vector2 cell = new Vector2(x, currentY);
                
                switch (currentMode)
                {
                    case CellUpdateMode.Increment:
                        if (GetPerlinSample(x + perlinSeedPosition, currentY + perlinSeedPosition) > perlinRain)
                        {
                            IncrementWater(cell, Mathf.Max(0, GetPerlinSample(x - perlinSeedPosition, currentY - perlinSeedPosition) * perlinMultiplier), Time.deltaTime * timeMultiplier);
                            //Debug.Log("Rained: " + Mathf.PerlinNoise(x + perlinSeedPosition, currentY + perlinSeedPosition) + " with: " + Mathf.PerlinNoise(x - perlinSeedPosition, currentY - perlinSeedPosition) * perlinMultiplier);
                        }
                        break;

                    case CellUpdateMode.Fluxes:
                        CalculateFluxes(cell, Time.deltaTime * timeMultiplier);
                        ScaleFluxes(cell);
                        break;

                    case CellUpdateMode.Field:
                        FieldUpdate(cell, Time.deltaTime * timeMultiplier);
                        break;

                    case CellUpdateMode.Erode:
                        CalculateVelocity(cell);
                        ErodeCell(cell);
                        break;

                    case CellUpdateMode.Transport:
                        TransportSediment(cell, Time.deltaTime * timeMultiplier);
                        break;

                    case CellUpdateMode.Evaporate:
                        EvaporateWater(cell, Time.deltaTime * timeMultiplier);
                        break;

                    case CellUpdateMode.Render:
                        terrain.SetHeight(x, currentY, GetVertexHeight(cell));
                        break;
                } 
            }
        }
        currentY++;
        if (currentMode == CellUpdateMode.Increment) perlinMovingPosition += seedIncrement;
        if (currentY == size)
        {
            currentY = 0;
            if ((int)currentMode < 6) currentMode += 1;
            else
            {
                currentMode = 0;
                terrain.regenerateMeshes = true;
            }
        }
    }

    void IncrementWater(Vector2 cell, float intensity, float Δt)
    {
        grid[(int)cell.x, (int)cell.y].waterHeight += Δt * intensity;
    }

    float GetFlux(Vector2 cell, FluxMode mode, float Δt)
    {
        switch (mode)
        {
            case FluxMode.LeftFlux:
                return Mathf.Max(0,
                    grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux] + Δt * (pipeLength * pipeLength) * ((Physics.gravity.y * HeightDifference(cell, FluxMode.LeftFlux)) / pipeLength));
            case FluxMode.RightFlux:
                return Mathf.Max(0,
                    grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux] + Δt * (pipeLength * pipeLength) * ((Physics.gravity.y * HeightDifference(cell, FluxMode.RightFlux)) / pipeLength));
            case FluxMode.UpFlux:
                return Mathf.Max(0,
                    grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.UpFlux] + Δt * (pipeLength * pipeLength) * ((Physics.gravity.y * HeightDifference(cell, FluxMode.UpFlux)) / pipeLength));
            case FluxMode.DownFlux:
                return Mathf.Max(0,
                    grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.DownFlux] + Δt * (pipeLength * pipeLength) * ((Physics.gravity.y * HeightDifference(cell, FluxMode.DownFlux)) / pipeLength));
            default:
                return 0;

        }
    }

    float HeightDifference(Vector2 cell, FluxMode mode)
    {
        switch (mode)
        {
            case FluxMode.LeftFlux:
                if ((int)cell.x - 1 < 0) return 0;
                return (grid[(int)cell.x, (int)cell.y].terrainHeight + grid[(int)cell.x, (int)cell.y].waterHeight - grid[(int)cell.x - 1, (int)cell.y].terrainHeight - grid[(int)cell.x - 1, (int)cell.y].waterHeight);
            case FluxMode.RightFlux:
                if ((int)cell.x + 1 >= size) return 0;
                return (grid[(int)cell.x, (int)cell.y].terrainHeight + grid[(int)cell.x, (int)cell.y].waterHeight - grid[(int)cell.x + 1, (int)cell.y].terrainHeight - grid[(int)cell.x + 1, (int)cell.y].waterHeight);
            case FluxMode.UpFlux:
                if ((int)cell.y - 1 < 0) return 0;
                return (grid[(int)cell.x, (int)cell.y].terrainHeight + grid[(int)cell.x, (int)cell.y].waterHeight - grid[(int)cell.x, (int)cell.y - 1].terrainHeight - grid[(int)cell.x, (int)cell.y - 1].waterHeight);
            case FluxMode.DownFlux:
                if ((int)cell.y + 1 >= size) return 0;
                return (grid[(int)cell.x, (int)cell.y].terrainHeight + grid[(int)cell.x, (int)cell.y].waterHeight - grid[(int)cell.x, (int)cell.y + 1].terrainHeight - grid[(int)cell.x, (int)cell.y + 1].waterHeight);
            default:
                return 0;
        }
    }

    void CalculateFluxes(Vector2 cell, float Δt)
    {
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux] = GetFlux(cell, FluxMode.LeftFlux, Δt);
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux] = GetFlux(cell, FluxMode.RightFlux, Δt);
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.UpFlux] = GetFlux(cell, FluxMode.UpFlux, Δt);
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.DownFlux] = GetFlux(cell, FluxMode.DownFlux, Δt);
    }

    void ScaleFluxes(Vector2 cell)
    {
        float scaleFactor = Mathf.Min(1, (grid[(int)cell.x, (int)cell.y].waterHeight * (pipeLength * pipeLength)) / (grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux] + grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux] + grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.UpFlux] + grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.DownFlux]));
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux] *= scaleFactor;
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux] *= scaleFactor;
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.UpFlux] *= scaleFactor;
        grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.DownFlux] *= scaleFactor;
    }

    void UpdateWater(Vector2 cell, float ΔV)
    {
        grid[(int)cell.x, (int)cell.y].waterHeight += ΔV / (pipeLength * pipeLength);
    }

    float NeightborFluxTotals(Vector2 cell, float Δt)
    {
        float flux = 0;
        if ((int)cell.x - 1 >= 0) flux += grid[(int)cell.x - 1, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux];
        if ((int)cell.x + 1 < size) flux += grid[(int)cell.x + 1, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux];
        if ((int)cell.y - 1 >= 0) flux += grid[(int)cell.x, (int)cell.y - 1].outflowFlux[(int)FluxMode.UpFlux];
        if ((int)cell.y + 1 < size) flux += grid[(int)cell.x, (int)cell.y + 1].outflowFlux[(int)FluxMode.DownFlux];
        return flux;
    }

    float FluxTotals(Vector2 cell)
    {
        float flux = 0;
        for (int i = 0; i < 4; i++) flux += grid[(int)cell.x, (int)cell.y].outflowFlux[i];
        return flux;
    }

    void FieldUpdate(Vector2 cell, float Δt)
    {
        float ΔV = Δt * (NeightborFluxTotals(cell, Δt) - FluxTotals(cell));
        UpdateWater(cell, ΔV);
    }

    float WaterΔ(Vector2 cell, bool yAxis)
    {
        float result = 0;
        switch(yAxis)
        {
            case false:
                if ((int)cell.x - 1 >= 0) result += grid[(int)cell.x - 1, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux];
                result -= grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux];
                result += grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.RightFlux];
                if ((int)cell.x + 1 < size) result -= grid[(int)cell.x + 1, (int)cell.y].outflowFlux[(int)FluxMode.LeftFlux];
                break;

            case true:
                if ((int)cell.y - 1 >= 0) result += grid[(int)cell.x, (int)cell.y - 1].outflowFlux[(int)FluxMode.DownFlux];
                result -= grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.UpFlux];
                result += grid[(int)cell.x, (int)cell.y].outflowFlux[(int)FluxMode.DownFlux];
                if ((int)cell.y + 1 < size) result -= grid[(int)cell.x, (int)cell.y + 1].outflowFlux[(int)FluxMode.UpFlux];
                break;
        }
         
        return result;
    }

    void CalculateVelocity(Vector2 cell)
    {
        float averageWaterHeight = (grid[(int)cell.x, (int)cell.y].lastWaterHeight + grid[(int)cell.x, (int)cell.y].waterHeight) / 2;
        float xComponent = (WaterΔ(cell, false) / pipeLength) / averageWaterHeight;
        float yComponent = (WaterΔ(cell, true) / pipeLength) / averageWaterHeight;
        grid[(int)cell.x, (int)cell.y].velocity = new Vector2(xComponent, yComponent);
    }

    float GetAngle(Vector2 cell)
    {

        float angle = 0;
        if (PointOutOfBounds(new Vector2((int)cell.x + 1, (int)cell.y)) || PointOutOfBounds(new Vector2((int)cell.x - 1, (int)cell.y)) ||
            PointOutOfBounds(new Vector2((int)cell.x, (int)cell.y + 1)) || PointOutOfBounds(new Vector2((int)cell.x, (int)cell.y - 1)))
        {
            angle = 0;
        }

        else
        {
            float x = (grid[(int)cell.x + 1, (int)cell.y].terrainHeight - grid[(int)cell.x - 1, (int)cell.y].terrainHeight) / 2;
            float y = (grid[(int)cell.x, (int)cell.y + 1].terrainHeight - grid[(int)cell.x, (int)cell.y - 1].terrainHeight) / 2;
            angle = Mathf.Sqrt(x * x + y * y) / Mathf.Sqrt(1 + (x * x) + (y * y));
        }
        return (angle > 65) ? 65 : angle;
    }

    float TransportCapacity(Vector2 cell)
    {
        Vector2 velocity = grid[(int)cell.x, (int)cell.y].velocity;
        float magnitude = 0;
        if (!(velocity.x == 0 && velocity.y == 0)) magnitude = velocity.magnitude;

        float capacity = sedimentCapacityConstant * GetAngle(cell) * magnitude;
        return (capacity > 0) ? capacity : minimumTransportCapacity; 
    }

    void ErodeCell(Vector2 cell)
    {
        float capacity = TransportCapacity(cell);
        if (capacity > grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount)
        {
            grid[(int)cell.x, (int)cell.y].terrainHeight = grid[(int)cell.x, (int)cell.y].terrainHeight - sedimentDissolvingConstant * (TransportCapacity(cell) - grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount);
            grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount = grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount + sedimentDissolvingConstant * (TransportCapacity(cell) - grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount);
        }

        else
        {
            grid[(int)cell.x, (int)cell.y].terrainHeight = grid[(int)cell.x, (int)cell.y].terrainHeight + sedimentDepositionConstant * (grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount - capacity);
            grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount = grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount - sedimentDepositionConstant * (grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount - capacity);
        }
    }

    void TransportSediment(Vector2 cell, float Δt)
    {
        //st +∆t(x, y) = s1(x − u · ∆t, y − v · ∆t)
        Vector2 velocity = grid[(int)cell.x, (int)cell.y].velocity;
        int posx = (int)cell.x + (int)(velocity.x * Δt);
        int posy = (int)cell.y + (int)(velocity.y * Δt);
        if (posx >= size) posx = size - 1;
        if (posy >= size) posy = size - 1;
        if (posx < 0) posx = 0;
        if (posy < 0) posy = 0;
        grid[(int)cell.x, (int)cell.y].suspendedSedimentAmount = grid[posx, posy].lastSuspendedSedimentAmount;
    }

    void EvaporateWater(Vector2 cell, float Δt)
    {
        //dt+∆t(x, y) = d2(x, y) · (1 − Ke · ∆t)
        grid[(int)cell.x, (int)cell.y].waterHeight = grid[(int)cell.x, (int)cell.y].waterHeight * (1 - waterEvaporationConstant * Δt);
    }

    bool PointOutOfBounds(Vector2 cell)
    {
        return ((int)cell.x >= size || (int)cell.x < 0) || ((int)cell.y >= size || (int)cell.y < 0);
    }

    float GetVertexHeight(Vector2 point)
    {
        //int x0 = 0;
        //if ((int)point.x > 0) x0 = (int)point.x - 1;
        //else x0 = (int)point.x;
        //
        //int y0 = 0;
        //if ((int)point.y > 0) y0 = (int)point.y - 1;
        //else y0 = (int)point.y;
        //
        //int x1 = 0;
        //if ((int)point.x < size - 1) x1 = (int)point.x + 1;
        //else x0 = (int)point.x;
        //
        //int y1 = 0;
        //if ((int)point.y < size - 1) y1 = (int)point.y + 1;
        //else y1 = (int)point.y;
        float totals = 0;
        int pointCount = 0;
        if (!PointOutOfBounds(new Vector2(point.x - 1, point.y - 1)))
        {
            totals += grid[(int)point.x - 1, (int)point.y - 1].terrainHeight;
            pointCount++;
        }
        if (!PointOutOfBounds(new Vector2(point.x, point.y)))
        {
            totals += grid[(int)point.x, (int)point.y].terrainHeight;
            pointCount++;
        }
        if (!PointOutOfBounds(new Vector2(point.x, point.y - 1)))
        {
            totals += grid[(int)point.x, (int)point.y - 1].terrainHeight;
            pointCount++;
        }
        if (!PointOutOfBounds(new Vector2(point.x - 1, point.y)))
        {
            totals += grid[(int)point.x - 1, (int)point.y].terrainHeight;
            pointCount++;
        }


        return totals / pointCount;
    }

    float GetPerlinSample(float x, float y)
    {
        float frequency = 4 / (float)size;
        float height = Mathf.PerlinNoise((float)x * frequency + perlinSeed, (float)y * frequency + perlinSeed) * 0.5f;

        return height;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.DrawWireCube(transform.position, new Vector3(size, pipeLength, size));

        if (grid != null)
        {

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (grid[x, y] != null)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawWireCube(new Vector3(x - size / 2, grid[x, y].terrainHeight, y - size / 2), Vector3.one * (pipeLength));

                        if (grid[x, y].waterHeight > 0)
                        {
                            Gizmos.color = Color.blue;
                            Gizmos.DrawWireCube(new Vector3(x - size / 2, (grid[x, y].waterHeight * 4) + grid[x, y].terrainHeight + 4, y - size / 2), Vector3.one * (pipeLength - 0.04f));
                        }
                    }
                }
            }
        }
    }
}

public class ErosionCell
{
    public float currentTerrainHeight { get; private set; }
    public float terrainHeight { get { return currentTerrainHeight; } set { currentTerrainHeight = Mathf.Max(0, value); } }
    public float lastWaterHeight { get; private set; }
    public float currentWaterHeight { get; private set; }
    public float waterHeight { get { return currentWaterHeight; } set { lastWaterHeight = currentWaterHeight; currentWaterHeight = Mathf.Max(0, ((!float.IsNaN(value)) ? value : 0)); } }
    public float lastSuspendedSedimentAmount { get; private set; }
    public float currentSuspendedSedimentAmount { get; private set; }
    public float suspendedSedimentAmount  { get { return currentSuspendedSedimentAmount; } set { lastSuspendedSedimentAmount = currentSuspendedSedimentAmount; currentSuspendedSedimentAmount = value; } }
    public float[] outflowFlux = new float[4];
    public Vector2 velocity = Vector2.zero;
}