
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
using System;
using System.Collections.Generic;

public class ProceduralTerrain : MonoBehaviour {

    #region Terrain
    //Options
    public bool generateOnStart = false;

    //Terrain Configuration
    public int width = 10;
	public int multiMeshWidth = 3;
	public float spacing = 1;
	public float textureMultiplier = 2f;
	public float heightMultiplier = 10f;
	public float waterLevel = 0.2f;
	public float waterDepthMultiplier = 0.6f;
    public Shader terrainShader;

    //Terrain Storage
    public List<MeshDataEntry> meshes;
    public Dictionary<int, Dictionary<int, MeshDataEntry>> meshMatrix;

	//Terrain Texturing
	public Color lakeSand = new Color (0.984f, 0.973f, 0.937f);
	public Color ground = new Color (0.105882f, 0.050980f, 0.309803f);
    public Texture2D terrainTexture;
    
    //Terrain Arrays
	GameObject[] gameObjectArray;
	Texture2D[] textureArray;
	Material[] materialArray;
	MeshFilter[] meshArray;

	//Terrain Statistics
	double beginTime;
	public readonly double generationTime;

    //Update-based building
    int currentZ = 0;
    int currentColumn = 0;
    int currentRow = 0;
    int currentSector = 0;

    bool buildMesh = false;
    bool buildingMesh = false;
    bool meshesBuilt = false;
    public bool generationDone { get; private set; }
    List<UpdateGenerationEntry> meshGenerationList;
    Queue<MeshDataEntry> meshRegenerationQueue;
    public bool regenerateMeshes;

    //UBB - Tracking
    int texOffsetX;
    int texOffsetY;

    List<Vector3[]> verts;
    List<int> tris;
    List<Vector2> uvs;
    #endregion

    void Start()
	{
        if (terrainShader == null)
        {
            terrainShader = Shader.Find("Standard");
        }

        //Generate terrain on start if set
        if (generateOnStart)
        {
            GenerateTerrain();
        }
	}

    void ResetGenerator()
    {
        meshGenerationList = new List<UpdateGenerationEntry>();
        meshRegenerationQueue = new Queue<MeshDataEntry>();
        meshes = new List<MeshDataEntry>();
        meshMatrix = new Dictionary<int, Dictionary<int, MeshDataEntry>>();
        meshesBuilt = false;
        generationDone = false;
        texOffsetX = 0;
        texOffsetY = 0;
    }

    void Update()
    {
        if (generationDone)
        {
            if (regenerateMeshes)
            {
                while (meshRegenerationQueue.Count > 0)
                {
                    RegenerateMesh(meshRegenerationQueue.Dequeue());
                }
                SetTextures();
                regenerateMeshes = false;
            }
            return;
        }


        if (buildMesh && meshGenerationList.Count == 0)
        {
            //No more meshes to generate
            buildMesh = false;
            meshesBuilt = true;
        }

        if (meshesBuilt)
        {
            //Meshes have been built - final steps
            SetTextures();

            //Set generation as completed
            generationDone = true;

            //Log generation time
            //double diff = Time.realtimeSinceStartup - beginTime;
            //Debug.Log("ProceduralTerrain Area " + this.name + " was generated in " + diff + " seconds.");
        }


        if (buildMesh && !buildingMesh)
        {
            UpdateSetupMesh();
        }

        if (buildMesh)
        {
            UpdateGenerateMesh();
        }
    }

    public void SetHeight(int sector, int x, int z, float height)
    {
        if (generationDone)
        {
            meshes[sector].verts[x][z].y = height;
            int OffsetX = (width - 1) * meshes[sector].row;
            int OffsetY = (width - 1) * meshes[sector].column;
            SetTextureColor(x + OffsetX, z + OffsetY, height);
            meshRegenerationQueue.Enqueue(meshes[sector]);
        }
    }

    public void SetHeight(int column, int row, int x, int z, float height)
    {
        if (meshMatrix.ContainsKey(row) && meshMatrix[row].ContainsKey(column)) meshMatrix[row][column].verts[x][z].y = height;
        int OffsetX = (width - 1) * row;
        int OffsetY = (width - 1) * column;
        SetTextureColor(x + OffsetX, z + OffsetY, height);
        meshRegenerationQueue.Enqueue(meshMatrix[row][column]);
    }

    // This shit is crazy
    public void SetHeight(int x, int z, float height)
    {
        if (!generationDone) return;

        int row = (int)(Math.Floor(z / (double)width));
        int column = (int)(Math.Floor(x / (double)width));

        int localX = ((x - column * width) + (1 * column));
        int localZ = ((z - row * width) + (1 * row));

        AdjustLocalCoordinates(ref localX, ref localZ, ref row, ref column);

        if (localX == width - 1) SetHeight(column + 1, row, 0, localZ, height);
        if (localZ == width - 1) SetHeight(column, row + 1, localX, 0, height);
        if ((localX == width - 1) && (localZ == width - 1)) SetHeight(column + 1, row + 1, 0, 0, height);

        if (meshMatrix.ContainsKey(row) && meshMatrix[row].ContainsKey(column) && (localX < width && localZ < width))
        {
            meshMatrix[row][column].verts[localX][localZ].y = height;
            int OffsetX = (width - 1) * row;
            int OffsetY = (width - 1) * column;
            SetTextureColor(localX + OffsetX, localZ + OffsetY, height);
            meshRegenerationQueue.Enqueue(meshMatrix[row][column]);
        }
    }

    void AdjustLocalCoordinates(ref int localX, ref int localZ, ref int row, ref int column)
    {
        //Debug.Log(string.Format("Adjusting Coordinates: {0},{1} in {2},{3}", localX, localZ, row, column));
        bool reduced = false;

        if (localX > width)
        {
            localX -= width - 1;
            column++;
            reduced = true;
        }

        if (localZ > width)
        {
            localZ -= width - 1;
            row++;
            reduced = true;
        }

        if (localX == width)
        {
            localX -= width - 1;
            column++;
            reduced = true;
        }

        if (localZ == width)
        {
            localZ -= width - 1;
            row++;
            reduced = true;
        }

        //Debug.Log(string.Format("Adjusted Coordinates: {0},{1} in {2},{3}", localX, localZ, row, column));
        if (reduced) AdjustLocalCoordinates(ref localX, ref localZ, ref row, ref column);
    }

    void AddUpdateGenerator(int row, int column, int currentSector)
    {
        meshGenerationList.Add(new UpdateGenerationEntry(row, column, currentSector));
    }

    Vector3[] UnfoldVertices(List<Vector3[]> verts)
    {
        Vector3[] unfolded_verts = new Vector3[width * width];
        int i = 0;
        foreach (Vector3[] v in verts)
        {
            v.CopyTo(unfolded_verts, i * width);
            i++;
        }

        return unfolded_verts;
    }

	void GenerateTerrain ()
	{
        //Set up generator
        GeneratorSetup();

        //Generate the Meshs
        int currentSector = 0;
		for (int row = 0; row < multiMeshWidth; row ++) {
			for (int column = 0; column < multiMeshWidth; column++) {

                //Set Transform
                Vector3 newTransform = new Vector3(row * width * spacing - row * spacing, 0, column * width * spacing - column * spacing);
                gameObjectArray[currentSector].transform.position = newTransform;

                //Add update generator entry
                AddUpdateGenerator(row, column, currentSector);

				currentSector++;
            }
		}

        //Set mesh generation flag
        buildMesh = true;
	}

    void GeneratorSetup()
    {
        //Set begin time
        //beginTime = Time.realtimeSinceStartup;

        //Kill any children
        foreach (Transform child in this.transform)
        {
            GameObject.Destroy(child.gameObject);
        }

        //Make terrainTexture
        terrainTexture = new Texture2D(width * multiMeshWidth, width * multiMeshWidth);

        //Make Arrays
        gameObjectArray = new GameObject[multiMeshWidth * multiMeshWidth];
        meshArray = new MeshFilter[multiMeshWidth * multiMeshWidth];
        materialArray = new Material[multiMeshWidth * multiMeshWidth];
        textureArray = new Texture2D[multiMeshWidth * multiMeshWidth];

        //Set Planes and Meshes
        for (int sector = 0; sector < (multiMeshWidth * multiMeshWidth); sector++)
        {
            
            materialArray[sector] = new Material(terrainShader); //Set up Material with terrainShader

            //Set up GameObject
            gameObjectArray[sector] = new GameObject();
            gameObjectArray[sector].transform.parent = this.transform;
            gameObjectArray[sector].name = "Sector" + sector;

            //Add Mesh Renderer and set material
            gameObjectArray[sector].AddComponent<MeshRenderer>();
            gameObjectArray[sector].GetComponent<MeshRenderer>().material = materialArray[sector];

            //Add Mesh Filter and place in array
            gameObjectArray[sector].AddComponent<MeshFilter>();
            meshArray[sector] = gameObjectArray[sector].GetComponent<MeshFilter>();
            meshArray[sector].mesh = new Mesh();

            //Add Mesh Collider
            gameObjectArray[sector].AddComponent<MeshCollider>();
            materialArray[sector].SetFloat("_Glossiness", 0.0f);

            ResetGenerator(); //Reset the generator values
        }
    }

	void SetTextures()
	{
		for (int currentSector = 0; currentSector < (multiMeshWidth * multiMeshWidth); currentSector++) {
			//Set texture
			materialArray[currentSector].mainTexture = terrainTexture;

			//Set texture offset
			float offset = (1f/(float)multiMeshWidth * (spacing * 0.5f)/spacing);
			Vector2 textureOffset = new Vector2(offset, offset);
			materialArray[currentSector].SetTextureOffset( "_MainTex", textureOffset);
		}
	}

    void RegenerateMesh(MeshDataEntry meshData)
    {
        // Unfold the 2d array of verticies into a 1d array.
        Vector3[] unfolded_verts = UnfoldVertices(meshData.verts);

        // Set mesh data.
        meshArray[meshData.sector].mesh.vertices = unfolded_verts;

        // Assign the mesh object and update it.
        meshArray[meshData.sector].mesh.RecalculateBounds();
        meshArray[meshData.sector].mesh.RecalculateNormals();
    }

	void UpdateGenerateMesh ()
	{
        if (currentZ == width)
        {
            //Done generating mesh - add and clean up
            UpdateFinishMesh();
            return;
        }


        verts.Add(new Vector3[width]);
        for (int x = 0; x < width; x++)
        {
            Vector3 current_point = new Vector3();

            current_point.x = (x * spacing) - (width / 2f * spacing);
            current_point.z = currentZ * spacing - (width / 2f * spacing);
            current_point.y = 0;

            //SetTextureColor(x, z, current_point.y, sector);
            SetTextureColor(x + texOffsetX, currentZ + texOffsetY, 0);

            verts[currentZ][x] = current_point;

            uvs.Add(new Vector2((current_point.x + (texOffsetX * spacing)) / (spacing * width * multiMeshWidth), (current_point.z + (texOffsetY * spacing)) / (spacing * width * multiMeshWidth)));
            //uvs.Add(new Vector2(current_point.x / (spacing * (width - 1)), current_point.z / (spacing * (width - 1))));

            //Christopher Andrews' improved triangle code
            int triID = x + currentZ * width;

            if (x != (width - 1) && currentZ != (width - 1))
            {
                tris.Add(triID);
                tris.Add(triID + width);
                tris.Add(triID + width + 1);

                tris.Add(triID);
                tris.Add(triID + width + 1);
                tris.Add(triID + 1);
            }
        }

        currentZ++;
    }

    void UpdateSetupMesh()
    {
        //Starting mesh generation - set everything up
        buildingMesh = true;

        //Set container lists
        verts = new List<Vector3[]>();
        uvs = new List<Vector2>();
        tris = new List<int>();

        //Set current mesh location
        UpdateGenerationEntry entry = meshGenerationList[0];
        currentColumn = entry.column;
        currentRow = entry.row;
        currentSector = entry.sector;
        currentZ = 0;

        //Set texture offset
        texOffsetX = (width - 1) * currentRow;
        texOffsetY = (width - 1) * currentColumn;
    }

    void UpdateFinishMesh()
    {
        gameObjectArray[currentSector].AddComponent<MeshDataEntry>();
        MeshDataEntry mde = gameObjectArray[currentSector].GetComponent<MeshDataEntry>();

        mde.row = currentRow;
        mde.column = currentColumn;
        mde.sector = currentSector;
        mde.verts = verts;

        meshes.Add(mde);
        if (!meshMatrix.ContainsKey(mde.row)) meshMatrix[mde.row] = new Dictionary<int, MeshDataEntry>();
        meshMatrix[mde.row][mde.column] = mde;

        // Unfold the 2d array of verticies into a 1d array.
        Vector3[] unfolded_verts = UnfoldVertices(verts);

        // Set mesh data.
        meshArray[currentSector].mesh.vertices = unfolded_verts;
        meshArray[currentSector].mesh.triangles = tris.ToArray();
        meshArray[currentSector].mesh.uv = uvs.ToArray();

        // Assign the mesh object and update it.
        meshArray[currentSector].mesh.RecalculateBounds();
        meshArray[currentSector].mesh.RecalculateNormals();
        gameObjectArray[currentSector].GetComponent<MeshCollider>().sharedMesh = meshArray[currentSector].mesh;

        // Finished Building Mesh
        buildingMesh = false;

        //Remove generation entry
        meshGenerationList.RemoveAt(0);

        
    }

	public void SetTextureColor(int x, int y, float value) //, int sector)
	{
		Color color = ground;
			
		if (value <= waterLevel + 0.05) {
			color = lakeSand;
            // set pixels to cover the cliffs
            terrainTexture.SetPixel(x + 1, y, color);
            terrainTexture.SetPixel(x - 1, y, color);
            terrainTexture.SetPixel(x, y + 1, color);
            terrainTexture.SetPixel(x, y - 1, color);
        }

        terrainTexture.SetPixel(x, y, color);
        terrainTexture.Apply();
    }
}

public class UpdateGenerationEntry
{
    public int row { get; private set; }
    public int column { get; private set; }
    public int sector { get; private set; }

    public UpdateGenerationEntry(int row, int column, int sector)
    {
        this.row = row;
        this.column = column;
        this.sector = sector;
    }
}

public class MeshDataEntry : Component
{
    public int row;
    public int column;
    public int sector;
    public List<Vector3[]> verts;
}