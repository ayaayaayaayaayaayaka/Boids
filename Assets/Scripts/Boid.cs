/*
Copyright (c) 2019 Sebastian Lague
Released under the MIT license
https://github.com/SebLague/Boids/blob/master/LICENSE
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Boid : MonoBehaviour {

    BoidSettings settings;

    // State
    [HideInInspector]
    public Vector3 position;
    [HideInInspector]
    public Vector3 forward;
    Vector3 velocity;
    public int type;

    // To update:
    Vector3 acceleration;
    [HideInInspector]
    public Vector3 avgFlockHeading;
    [HideInInspector]
    public Vector3 avgAvoidanceHeading;
    [HideInInspector]
    public Vector3 centreOfFlockmates;
    [HideInInspector]
    public int numPerceivedFlockmates;

    // Cached
    Material material;
    public Material blueMat;
    Transform cachedTransform;         //transformへのアクセスは重いのでキャッシュする

    void Awake () {
        cachedTransform = transform;
    }

    public void Initialize (BoidSettings settings,int type) {
        this.settings = settings;
        this.type=type;
        if(type==1){
            transform.GetChild(1).GetComponent<SkinnedMeshRenderer>().material=blueMat;    //if two species exist, change color
        }
        

        position = cachedTransform.position;
        forward = cachedTransform.forward;

        float startSpeed = (settings.minSpeed + settings.maxSpeed) / 2;
        velocity = transform.forward * startSpeed;
    }

    public void UpdateBoid () {
        Vector3 acceleration = Vector3.zero;

        // 活動範囲の境界：半径50の球の内側に留める（3段階で対策）
        float boundaryRadius = 50f;
        float marginRadius = boundaryRadius * 0.85f;  // この距離から引き返し始める
        float distFromCenter = position.magnitude;
        if (distFromCenter > boundaryRadius) {
            // すでに外に出ている → 強い力で中心に戻す（Steerの上限を超えて効かせる）
            Vector3 returnDir = (Vector3.zero - position).normalized;
            acceleration += returnDir * Mathf.Min(settings.maxSteerForce * 5f, 20f);
        } else if (distFromCenter > marginRadius) {
            // 境界に近づいている → 中心方向のステアを強めにかける
            Vector3 returnDir = (Vector3.zero - position).normalized;
            float t = (distFromCenter - marginRadius) / (boundaryRadius - marginRadius);
            acceleration += SteerTowards(returnDir) * (10f + t * 15f);
        }

        GameObject predatorObj = GameObject.FindWithTag("Predator");
        if (predatorObj != null) {
            Vector3 offset = position - predatorObj.transform.position;
            // 15m以内に捕食者がいたら逃げる
            if (offset.sqrMagnitude < 15 * 15) {
                // SteerTowardsで逃げる力を計算
                acceleration += SteerTowards(offset) * 15.0f;
            }
        }

        if (numPerceivedFlockmates != 0) {
            centreOfFlockmates /= numPerceivedFlockmates;               //自分の周りにいる魚の重心を求める

            Vector3 offsetToFlockmatesCentre = (centreOfFlockmates - position);      //重心へのベクトル

            var alignmentForce = SteerTowards (avgFlockHeading) * settings.alignWeight;             //近くの魚が向かう方向に向かう力
            var cohesionForce = SteerTowards (offsetToFlockmatesCentre) * settings.cohesionWeight;  //近くの魚の重心へ向かう力
            var seperationForce = SteerTowards (avgAvoidanceHeading) * settings.seperateWeight;     //近づきすぎるのを避ける力

            acceleration += alignmentForce;
            acceleration += cohesionForce;
            acceleration += seperationForce;
        }

        if (IsHeadingForCollision ()) {
            Vector3 collisionAvoidDir = ObstacleRays ();         //障害物を避ける方向を取得
            Vector3 collisionAvoidForce = SteerTowards (collisionAvoidDir) * settings.avoidCollisionWeight;   //障害物を避ける力
            acceleration += collisionAvoidForce;
        }

        velocity += acceleration * Time.deltaTime;        //加速度を用いて速度を変更する。
        float speed = velocity.magnitude;
        Vector3 dir = velocity / speed;
        speed = Mathf.Clamp (speed, settings.minSpeed, settings.maxSpeed);      //速度のスカラが範囲内に収まるようにする
        velocity = dir * speed;

        cachedTransform.position += velocity * Time.deltaTime;
        cachedTransform.forward = dir;
        position = cachedTransform.position;
        forward = dir;

        // 最終手段：境界外に出ていたら球内に押し戻す（捕食者と同じ方式）
        if (position.magnitude > boundaryRadius) {
            position = position.normalized * boundaryRadius;
            cachedTransform.position = position;
        }
    }

    bool IsHeadingForCollision () {         //障害物が進む先にあるかどうかを判定
        RaycastHit hit;
        if (Physics.SphereCast (position, settings.boundsRadius, forward, out hit, settings.collisionAvoidDst, settings.obstacleMask)) {
            return true;
        } else { }
        return false;
    }

    Vector3 ObstacleRays () {                                //障害物がない方向ベクトルを取得
        Vector3[] rayDirections = BoidHelper.directions;     //ここに方向ベクトルの候補が格納される

        for (int i = 0; i < rayDirections.Length; i++) {
            Vector3 dir = cachedTransform.TransformDirection (rayDirections[i]);
            Ray ray = new Ray (position, dir);
            if (!Physics.SphereCast (ray, settings.boundsRadius, settings.collisionAvoidDst, settings.obstacleMask)) {
                return dir;          //rayの先に障害物がなかったらその方向を返す。
            }
        }

        return forward;
    }

    Vector3 SteerTowards (Vector3 vector) {                             //力が大きくなりすぎないように上から抑える
        Vector3 v = vector.normalized * settings.maxSpeed - velocity;
        return Vector3.ClampMagnitude (v, settings.maxSteerForce);
    }

}