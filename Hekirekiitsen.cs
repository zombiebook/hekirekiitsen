using System;
using System.Reflection;
using UnityEngine;

namespace HekirekiItsen
{
    // Duckov 모드 로더에 등록하는 엔트리
    internal class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void Awake()
        {
            Debug.Log("[HekirekiItsen.ModBehaviour] HekirekiItsenRunner 생성 시도");

            var go = new GameObject("HekirekiItsenRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<HekirekiItsenRunner>();

            Debug.Log("[HekirekiItsen.ModBehaviour] HekirekiItsenRunner 생성 완료");
        }
    }

    internal class HekirekiItsenRunner : MonoBehaviour
    {
        private KeyCode _activationKey = KeyCode.LeftAlt; // 발동 키
        private float _teleportDistance = 5f;             // 이동 거리
        private float _aoeRadius = 1f;                    // 범위 반경
        private float _fixedDamage = 10f;                 // 고정 대미지
        private float _cooldown = 0.3f;                   // 쿨타임(초)
        private float _nextAvailableTime;

        private void Update()
        {
            // 쿨타임 체크
            if (Time.time < _nextAvailableTime)
                return;

            // 발동 키 입력
            if (!Input.GetKeyDown(_activationKey))
                return;

            Debug.Log("[HekirekiItsen] 발동 키 입력 감지");

            Transform playerTf = FindPlayerTransform();
            if (playerTf == null)
            {
                Debug.Log("[HekirekiItsen] 플레이어를 찾지 못해 발동 취소");
                return;
            }

            // 입력 방향(WASD) + 없으면 카메라 기준
            Vector3 dir = GetFacingDirection();
            Debug.Log("[HekirekiItsen] 순간이동 방향: " + dir.ToString("F2"));

            TryTeleportAndStrike(playerTf, dir);

            _nextAvailableTime = Time.time + _cooldown;
        }

        // ───────────────────────────────── 플레이어 찾기 ─────────────────────────────────
        private Transform FindPlayerTransform()
        {
            // 1) CharacterMainControl.Main 먼저 사용
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    var mb = main as MonoBehaviour;
                    if (mb != null)
                    {
                        // root 로 올려서 전체 캐릭터를 통째로 이동
                        Transform t = mb.transform.root;
                        Debug.Log("[HekirekiItsen] CharacterMainControl.Main 사용해 플레이어 획득: " + t.name);
                        return t;
                    }

                    Debug.Log("[HekirekiItsen] CharacterMainControl.Main 는 MonoBehaviour가 아님");
                }
                else
                {
                    Debug.Log("[HekirekiItsen] CharacterMainControl.Main == null");
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[HekirekiItsen] CharacterMainControl.Main 접근 중 예외: " + ex);
            }

            // 2) 실패하면 씬 전체에서 CharacterMainControl 검색
            try
            {
                CharacterMainControl[] all = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (all != null && all.Length > 0)
                {
                    Transform best = null;
                    float bestScore = float.NegativeInfinity;
                    Camera cam = Camera.main;

                    foreach (var cmc in all)
                    {
                        if (cmc == null) continue;

                        var mb = cmc as MonoBehaviour;
                        if (mb == null) continue;

                        float score = 0f;
                        if (cam != null)
                        {
                            score = -Vector3.Distance(cam.transform.position, mb.transform.position);
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = mb.transform.root;
                        }
                    }

                    if (best != null)
                    {
                        Debug.Log("[HekirekiItsen] 플레이어 후보 선택: " + best.name + " (score=" + bestScore + ")");
                        return best;
                    }
                }
                else
                {
                    Debug.Log("[HekirekiItsen] 씬에 CharacterMainControl 이 하나도 없음");
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[HekirekiItsen] FindObjectsOfType<CharacterMainControl> 예외: " + ex);
            }

            Debug.Log("[HekirekiItsen] 플레이어를 끝내 찾지 못함, 기능 중단");
            return null;
        }

        // ──────────────────────────────── 이동 방향 계산 ────────────────────────────────
        private Vector3 GetFacingDirection()
        {
            // 상단 탑뷰 기준: XZ 평면에서 W/S/A/D
            Vector3 dir = Vector3.zero;

            if (Input.GetKey(KeyCode.W)) dir += new Vector3(0f, 0f, 1f);
            if (Input.GetKey(KeyCode.S)) dir += new Vector3(0f, 0f, -1f);
            if (Input.GetKey(KeyCode.A)) dir += new Vector3(-1f, 0f, 0f);
            if (Input.GetKey(KeyCode.D)) dir += new Vector3(1f, 0f, 0f);

            if (dir.sqrMagnitude > 0.001f)
            {
                dir.Normalize();
                Debug.Log("[HekirekiItsen] 입력 기반 방향 사용: " + dir.ToString("F2"));
                return dir;
            }

            // 입력이 없으면 카메라 forward 기준 (탑뷰에서도 혹시 모를 대비)
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 camForward = cam.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.0001f)
                {
                    camForward.Normalize();
                    Debug.Log("[HekirekiItsen] 입력 없음, 카메라 forward 사용: " + camForward.ToString("F2"));
                    return camForward;
                }
            }

            // 최악의 경우 월드 +Z
            Debug.Log("[HekirekiItsen] 방향 입력 / 카메라 없음, 기본값 (0,0,1) 사용");
            return Vector3.forward;
        }

        // ──────────────────────────────── 순간이동 + 공격 ────────────────────────────────
        private void TryTeleportAndStrike(Transform playerTf, Vector3 dir)
        {
            Vector3 startPos = playerTf.position;
            Vector3 targetPos = startPos + dir * _teleportDistance;

            // 벽에 박지 않도록 앞에 Raycast
            RaycastHit hit;
            if (Physics.Raycast(startPos + Vector3.up * 0.1f, dir, out hit, _teleportDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                targetPos = hit.point - dir * 0.1f;
                targetPos.y = startPos.y;
            }

            Debug.Log("[HekirekiItsen] 순간이동 시도: " + startPos + " -> " + targetPos);

            TeleportPlayer(playerTf, startPos, targetPos);
        }

        private void TeleportPlayer(Transform playerTf, Vector3 startPos, Vector3 targetPos)
        {
            // Rigidbody가 있으면 잠깐 kinematic으로 바꿨다가 복귀
            Rigidbody rb = playerTf.GetComponent<Rigidbody>();
            bool hadRb = rb != null;
            bool prevKinematic = false;

            if (hadRb)
            {
                prevKinematic = rb.isKinematic;
                rb.isKinematic = true;
            }

            playerTf.position = targetPos;

            if (hadRb)
            {
                rb.isKinematic = prevKinematic;
            }

            Debug.Log("[HekirekiItsen] Transform 기반 순간이동 완료: " + targetPos);

            SpawnSmokeTrail(startPos, targetPos);
            ApplyAoEDamage(playerTf, targetPos);
        }

        // ──────────────────────────────── 범위 대미지 ────────────────────────────────
        private void ApplyAoEDamage(Transform playerTf, Vector3 center)
        {
            Collider[] cols = Physics.OverlapSphere(center, _aoeRadius, ~0, QueryTriggerInteraction.Collide);
            Debug.Log("[HekirekiItsen] AoE 검사 시작 - 반경 " + _aoeRadius + "m, collider 수: " + cols.Length);

            int hitCount = 0;

            foreach (var col in cols)
            {
                if (col == null) continue;

                Debug.Log("[HekirekiItsen]  Collider: " + col.name + " / " + col.gameObject.name);

                // 자기 자신(플레이어)은 스킵
                if (col.attachedRigidbody != null && col.attachedRigidbody.transform == playerTf)
                {
                    Debug.Log("[HekirekiItsen]   → 플레이어, 스킵");
                    continue;
                }

                if (TryApplyDamage(col.gameObject, _fixedDamage))
                    hitCount++;
            }

            Debug.Log("[HekirekiItsen] AoE 대미지 종료: " + hitCount + " 개 대상에게 " + _fixedDamage + " 대미지");
        }

        private bool TryApplyDamage(GameObject target, float damage)
        {
            try
            {
                Component health = target.GetComponent("Health");
                if (health == null)
                    return false;

                Type t = health.GetType();
                MethodInfo m =
                    t.GetMethod("TakeDamage", new Type[] { typeof(float) }) ??
                    t.GetMethod("Damage", new Type[] { typeof(float) }) ??
                    t.GetMethod("ApplyDamage", new Type[] { typeof(float) });

                if (m != null)
                {
                    m.Invoke(health, new object[] { damage });
                    Debug.Log("[HekirekiItsen]   → " + target.name + " 에게 " + damage + " 대미지 시도 (메서드: " + m.Name + ")");
                    return true;
                }

                Debug.Log("[HekirekiItsen]   → " + target.name + " Health 발견했지만 Damage 메서드 없음");
            }
            catch (Exception ex)
            {
                Debug.Log("[HekirekiItsen]   → 대미지 처리 중 예외: " + ex);
            }

            return false;
        }

        // ──────────────────────────────── 번개 트레일 이펙트 ────────────────────────────────
        private void SpawnSmokeTrail(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist <= 0.01f)
                return;

            dir /= dist;

            int steps = 3;
            float step = dist / (steps + 1);
            int countPerStep = 8;

            for (int i = 1; i <= steps; i++)
            {
                Vector3 pos = from + dir * (step * i);
                SpawnMiniSmoke(pos, countPerStep);
            }
        }

        private void SpawnMiniSmoke(Vector3 pos, int count)
        {
            GameObject go = new GameObject("HekirekiTrail");
            go.transform.position = pos;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 0.35f;
            main.startSpeed   = 0.3f;
            main.startSize    = 0.1f;
            main.startColor   = new Color(0.3f, 0.9f, 1f, 0.9f); // 번개 느낌 하늘색
            main.duration     = 0.4f;
            main.loop         = false;

            ps.emission.SetBursts(new ParticleSystem.Burst[]
            {
                new ParticleSystem.Burst(0f, (short)count)
            });

            var shape = ps.shape;
            shape.enabled  = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius   = 0.05f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.Local;
            vel.y       = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            vel.x       = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.z       = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

            // colorOverLifetime struct 수정은 이렇게 따로 받아서
            var col = ps.colorOverLifetime;
            col.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

            ps.Play();
            UnityEngine.Object.Destroy(go, 0.7f);
        }
    }
}
