/*
Copyright (c) 2019 Sebastian Lague
Released under the MIT license
https://github.com/SebLague/Boids/blob/master/LICENSE
*/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour {

    const int threadGroupSize = 1024;

    public BoidSettings settings;
    public ComputeShader compute;
    Boid[] boids;
    public bool existTwoSpecies;

    void Start () {
        boids = FindObjectsOfType<Boid> ();
        foreach (Boid b in boids) {
            int type=existTwoSpecies ? (int)Mathf.Round(Random.value) : 0;     //2つの種族がいる場合は50:50になるように設定
            b.Initialize (settings,type);
        }

    }

    void Update () {
        if (boids != null) {

            List<Boid> aliveBoids = new List<Boid>();
                    foreach (Boid b in boids) {
                        if (b != null) aliveBoids.Add(b);
                    }
                    boids = aliveBoids.ToArray();

            int numBoids = boids.Length;
            var boidData = new BoidData[numBoids];

            for (int i = 0; i < boids.Length; i++) {      //compute shader用のデータを格納
                boidData[i].position = boids[i].position;
                boidData[i].direction = boids[i].forward;
                boidData[i].type=boids[i].type;
            }

            var boidBuffer = new ComputeBuffer (numBoids, BoidData.Size);
            boidBuffer.SetData (boidData);

            compute.SetBuffer (0, "boids", boidBuffer);
            compute.SetInt ("numBoids", boids.Length);
            compute.SetFloat ("viewRadius", settings.perceptionRadius);
            compute.SetFloat ("avoidRadius", settings.avoidanceRadius);

            int threadGroups = Mathf.CeilToInt (numBoids / (float) threadGroupSize);
            compute.Dispatch (0, threadGroups, 1, 1);     //コンピュートシェーダーを実行

            boidBuffer.GetData (boidData);

            for (int i = 0; i < boids.Length; i++) {                
                boids[i].avgFlockHeading = boidData[i].flockHeading;
                boids[i].centreOfFlockmates = boidData[i].flockCentre;
                boids[i].avgAvoidanceHeading = boidData[i].avoidanceHeading;
                boids[i].numPerceivedFlockmates = boidData[i].numFlockmates;

                boids[i].UpdateBoid ();
            }

            boidBuffer.Release ();
        }
    }

    public struct BoidData {
        public Vector3 position;
        public Vector3 direction;
        public int type;

        public Vector3 flockHeading;
        public Vector3 flockCentre;
        public Vector3 avoidanceHeading;
        public int numFlockmates;

        public static int Size {
            get {
                return sizeof (float) * 3 * 5 + sizeof (int)*2;
            }
        }
    }
}