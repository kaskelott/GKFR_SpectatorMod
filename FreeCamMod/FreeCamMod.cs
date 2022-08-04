using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using BepInEx;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Photon.Pun;

namespace FreeCamMod
{
    [BepInPlugin("com.appwal.gkfr.miscmods", "Walrus GKFR Mods", "1.0.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            //Harmony.DEBUG = true;
            FileLog.Reset();
            this.h = new Harmony("com.appwal.gkfr.miscmods");   
            this.h.PatchAll(Assembly.GetExecutingAssembly());
            
        }

        private void Update()
        {
            bool keyDown = Input.GetKeyDown(KeyCode.F5);
            if (keyDown)
            {
                Plugin.run = !Plugin.run;
            }
        }

        private void OnDestroy()
        {
            this.h.UnpatchSelf();
        }

        private Harmony h;

        public static bool run = true;
    }

    [HarmonyPatch(typeof(RcVehiclePhysic), "Init")]
    public class AttachFreecam
    {
        public static void Postfix(RcVehiclePhysic __instance)
        {
            StatsModifiers component = __instance.GetComponent<StatsModifiers>();
            bool flag = !component.Driver.IsLocal || component.Driver.IsAi;
            if (!flag)
            {
                AttachFreecam.index = component.Driver.Id;
                friKamm = __instance.gameObject.AddComponent<FreeCam>();
                friKamm.body = __instance.GetVehicleBody();
                friKamm.driver = component.Driver;
            }
        }

        public static int index = -1;

        public static FreeCam friKamm = null;
    }


    [HarmonyPatch(typeof(Kart), "Respawn")]
    public class TurnOffRespawn
    {
        public static bool Prefix(Kart __instance)
        {
            if (Plugin.run && __instance.GetComponentInParent<Driver>().LocalHumanId == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(RcRespawnChecker), "CheckRespawn")]
    public class TurnOffRespawnCheck
    {
        public static bool Prefix(RcVehicle pVehicle, ref bool __result)
        {
            bool result;
            //FileLog.Log(pVehicle.GetComponentInParent<Driver>().LocalHumanId.ToString());
            if (!Plugin.run || pVehicle.GetComponentInParent<Driver>().LocalHumanId != 0)
            {
                result = true;
            }
            else
            {
                __result = false;
                result = false;
            }
            return result;
        }
    }

    [HarmonyPatch(typeof(GkNetTransformSync), "OnPhotonSerializeView")]
    public class RemoveKartServer
    {
        public static bool Prefix(GkNetTransformSync __instance, PhotonStream stream, PhotonMessageInfo info, GkNetTransformSync.ComponentToSynchronize ___m_componentToSynchronize, Rigidbody ___m_rigidBody, Transform ___m_transform, Vector3 ___m_velocity)
        {
            if (__instance.gameObject.GetComponent<Driver>() == null) return true;
            bool flag = !Plugin.run || __instance.gameObject.GetComponent<Driver>().LocalHumanId != 0 || !__instance.enabled || !stream.IsWriting;
            if (flag)
            {
                return true;
            }
            else
            {
                bool flag2 = ___m_componentToSynchronize == GkNetTransformSync.ComponentToSynchronize.RIGIDBODY;
                if (flag2)
                {
                    
                    stream.SendNext(new Vector3(300f, 300f, 300f));  
                    stream.SendNext(___m_rigidBody.velocity);
                    stream.SendNext(___m_rigidBody.rotation);
                    return false;
                }
                else
                {
                    AccessTools.Method(typeof(GkNetTransformSync), "UpdatePreviousPositionAndVelocityFromTransform", null, null).Invoke(__instance, new object[] { });
                    stream.SendNext(new Vector3(300f, 300f, 300f));
                    stream.SendNext(___m_velocity);
                    stream.SendNext(___m_transform.rotation);
                    return false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(GkNetTransformSync), "MoveTowardsFuturePosition")]
    class SmoothFollower
    {
        static bool Prefix(GkNetTransformSync __instance, double interpolationTime, Transform ___m_transform, Vector3 ___m_velocity, Rigidbody ___m_rigidBody,int ___m_sceneryLayerMask, GkNetTransformSync.ComponentToSynchronize ___m_componentToSynchronize)
        {
            //FileLog.Log(__instance.GetComponentInParent<Driver>().LocalHumanId.ToString());
            if (__instance.GetComponentInParent<Driver>() == null) return true;
            Driver target = __instance.GetComponentInParent<Driver>();
            if (target.LocalHumanId == 0 || (!GkNetMgr.Instance.IsMasterClient && AttachFreecam.friKamm.following != target.Id-1) || (GkNetMgr.Instance.IsMasterClient && !target.IsAi && AttachFreecam.friKamm.following != target.LocalId))
            {
                return true;
            }
            float value = GamePreferences.NetNewSyncExtrapolationTime.Value;
            double time = interpolationTime + (double)value;
            float t = Time.fixedDeltaTime / value;

            object state = AccessTools.Method(typeof(GkNetTransformSync), "ComputeState", null, null).Invoke(__instance, new object[] { time });
            var fields = state.GetType().GetRuntimeFields();
            double fieldTid = (double)fields.ElementAt(0).GetValue(state);
            Vector3 fieldPos = (Vector3)fields.ElementAt(1).GetValue(state);
            Vector3 fieldVel = (Vector3)fields.ElementAt(2).GetValue(state);
            Quaternion fieldRot = (Quaternion)fields.ElementAt(3).GetValue(state);

            Vector3 vector = Vector3.Lerp(___m_transform.position, fieldPos, t);
            Quaternion rotation = Quaternion.Slerp(___m_transform.rotation, fieldRot, t);
            Vector3 velocity = Vector3.Lerp(___m_velocity, fieldVel, t);

            Vector3 position = Vector3.Lerp(AttachFreecam.friKamm.body.transform.position, fieldPos, t);
            Quaternion rotation2 = Quaternion.Slerp(AttachFreecam.friKamm.body.transform.rotation, fieldRot, t);
            AttachFreecam.friKamm.body.transform.position = position;
            AttachFreecam.friKamm.body.transform.rotation = rotation2;
            AttachFreecam.friKamm.body.position = position;
            AttachFreecam.friKamm.body.rotation = rotation2;
            AttachFreecam.friKamm.body.velocity = Vector3.Lerp(AttachFreecam.friKamm.body.velocity, fieldVel, t);

            if (___m_rigidBody != null)
            {
                Vector3 vector2 = vector - ___m_transform.position;
                RaycastHit raycastHit;
                if (___m_rigidBody.SweepTest(vector2, out raycastHit, vector2.magnitude, QueryTriggerInteraction.Ignore) && (1 << raycastHit.collider.gameObject.layer & ___m_sceneryLayerMask) != 0)
                {
                    Vector3 normal = raycastHit.normal;
                    float d = -Vector3.Dot(vector2, normal);
                    vector += normal * d;
                }
            }
            if (___m_componentToSynchronize == GkNetTransformSync.ComponentToSynchronize.RIGIDBODY)
            {
                ___m_rigidBody.position = vector;
                ___m_rigidBody.rotation = rotation;
                ___m_rigidBody.velocity = velocity;
                
            }
            else
            {
                ___m_transform.position = vector;
                ___m_transform.rotation = rotation;
            }            
            ___m_velocity = velocity;

            return false;

        }
       
    }

    [HarmonyPatch(typeof(KartFxMgr), "Update")]
    public class ClientRemoveLocalKartFX
    {
        private static bool Prefix(KartFxMgr __instance, ref Kart ___m_pKart, ref List<PipeFx> ___m_smokePipes)
        {
            if (___m_pKart == null)
            {
                return false;
            }
            if (___m_pKart.Driver.LocalHumanId == 0 && Plugin.run)
            {
                AccessTools.Method(typeof(KartFxMgr), "StopPipesFx", null, null).Invoke(__instance, new object[]
                {
                    ___m_smokePipes
                });
                __instance.StopAllFX();
                return false;
            }
            return true;
        }
    }

    


    [HarmonyPatch(typeof(PlayerBuilder), "Build")]
    public class ClientRemoveLocalKart
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
        {
            List<CodeInstruction> instrs = new List<CodeInstruction>(instructions);
            CodeMatcher matcher = new CodeMatcher(instructions);
     
            

            int gameobject1;
            int gameobject2;
            int gameobject3;

            matcher
                .MatchForward(true,
                new CodeMatch(i => i.opcode == OpCodes.Call && ((MethodInfo)i.operand).Name == nameof(PlayerGameEntities.GetKartObject)),
                new CodeMatch(i => i.opcode == OpCodes.Call && ((MethodInfo)i.operand).Name == nameof(UnityEngine.GameObject.Instantiate))
                
                );
            gameobject1 = (instrs[matcher.Pos + 1].operand as LocalBuilder).LocalIndex;
            FileLog.Log(gameobject1.ToString());

            matcher
                .MatchForward(true,
                new CodeMatch(i => i.opcode == OpCodes.Ldstr && ((string)i.operand) == "Kart_FX"),
                new CodeMatch(i => i.opcode == OpCodes.Newobj && ((ConstructorInfo)i.operand).Name == AccessTools.Constructor(typeof(GameObject)).Name && ((ConstructorInfo)i.operand).ReflectedType == typeof(GameObject))
                );
            gameobject2 = (instrs[matcher.Pos + 1].operand as LocalBuilder).LocalIndex;
            FileLog.Log(gameobject2.ToString());

            matcher
                .MatchForward(true,
                new CodeMatch(i => i.opcode == OpCodes.Call && ((MethodInfo)i.operand).Name == nameof(PlayerGameEntities.GetCharacterObject)),
                new CodeMatch(i => i.opcode == OpCodes.Call && ((MethodInfo)i.operand).Name == nameof(UnityEngine.GameObject.Instantiate))
                );
            gameobject3 = (instrs[matcher.Pos + 1].operand as LocalBuilder).LocalIndex;
            FileLog.Log(gameobject3.ToString());


            

            Label label = ilg.DefineLabel();
            LocalBuilder localvector = ilg.DeclareLocal(typeof(Vector3));
            bool flag2 = false;
            for (int i = 0; i < instrs.Count; i++)
            {
                
                CodeInstruction instruction = instrs[i];
                yield return instruction;
                FileLog.Log(instruction.ToString());              
                if (instruction.opcode == OpCodes.Stloc_S && (instruction.operand as LocalBuilder)?.LocalIndex == 13)
                {
                    flag2 = true;
                    FileLog.Log("Injection point found in PlayerBuilder.Build");
                    yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Plugin), nameof(Plugin.run)));
                    yield return new CodeInstruction(OpCodes.Brfalse_S, label);
                    yield return new CodeInstruction(OpCodes.Ldarg, 7);
                    yield return new CodeInstruction(OpCodes.Brtrue, label);
                    yield return new CodeInstruction(OpCodes.Ldloca_S, localvector.LocalIndex);
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Constructor(typeof(Vector3), new Type[]{typeof(float), typeof(float), typeof(float) }));
                    yield return new CodeInstruction(OpCodes.Ldloc, gameobject1);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GameObject), "transform"));
                    yield return new CodeInstruction(OpCodes.Ldloc, localvector);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(Transform), "localScale"));
                    yield return new CodeInstruction(OpCodes.Ldloc, gameobject2);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GameObject), "transform"));
                    yield return new CodeInstruction(OpCodes.Ldloc, localvector);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(Transform), "localScale"));
                    yield return new CodeInstruction(OpCodes.Ldloc, gameobject3);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GameObject), "transform"));
                    yield return new CodeInstruction(OpCodes.Ldloc, localvector);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(Transform), "localScale"));
                    yield return new CodeInstruction(OpCodes.Nop, null)
                    {
                        labels =
                        {
                            label
                        }
                    };

                }
            }
            
            if (object.Equals(false, flag2))
            {
                throw new ArgumentException("Injection point not found in PlayerBuilder.Build or F5 clicked");
            }
        }
    }

    
    [HarmonyPatch(typeof(Kart), "FixedUpdate")]
    class RemoveSomeHUD
    {
        static AccessTools.FieldRef<MenuHDPlayerRaceHUD, HUDBonusHD> hudbonus = AccessTools.FieldRefAccess<HUDBonusHD>(typeof(MenuHDPlayerRaceHUD), "m_hudBonus");
        static AccessTools.FieldRef<MenuHDPlayerRaceHUD, HUDLapCounterHD> hudlap = AccessTools.FieldRefAccess<HUDLapCounterHD>(typeof(MenuHDPlayerRaceHUD), "m_hudLapCounter");
        static AccessTools.FieldRef<MenuHDPlayerRaceHUD, HUDPositionHD> hudpos = AccessTools.FieldRefAccess<HUDPositionHD>(typeof(MenuHDPlayerRaceHUD), "m_hudPosition");
        static AccessTools.FieldRef<MenuHDPlayerRaceHUD, HUDRadarHD> hudrad = AccessTools.FieldRefAccess<HUDRadarHD>(typeof(MenuHDPlayerRaceHUD), "m_hudRadar");
        static void Postfix(Kart __instance)
        {
            if (Plugin.run && __instance.Driver.LocalHumanId == 0)
            {
                __instance.Hud.Position.WrongWay = false;
                hudbonus(__instance.Hud).gameObject.SetActive(false);
                hudlap(__instance.Hud).gameObject.SetActive(false);
                hudpos(__instance.Hud).gameObject.SetActive(false);
                //hudrad(__instance.Hud).gameObject.SetActive(false);
                //UnityEngine.Object.Destroy(__instance.GetComponent<RcVehiclePhysic>());
            }

        }
    }

    [HarmonyPatch(typeof(CamStateRear), "Update")]
    public class RearCamIsInfactTheFrontCam
    {
        static void Postfix(CamStateRear __instance, ref Transform ___m_rearTarget, float ___m_distance, float ___m_height, float ___m_leanAngle)
        {
            if (Plugin.run && FreeCam.friKamera)
            {
                ___m_rearTarget.localPosition = new Vector3(0f, 0.75f, -(___m_distance+0.5f));
                ___m_rearTarget.localRotation = Quaternion.Euler(0f, 0f, 0f);
            }
            else if (Plugin.run && !FreeCam.friKamera)
            {
                ___m_rearTarget.localPosition = new Vector3(0f, 1f, -(___m_distance + 2f));
                ___m_rearTarget.localRotation = Quaternion.Euler(0f, 0f, 0f);
            }
            else
            {
                ___m_rearTarget.localPosition = new Vector3(0f, ___m_height, -___m_distance);
                ___m_rearTarget.localRotation = Quaternion.Euler(-___m_leanAngle, 180f, 0f);
            }
               
        }
    }
    
    [HarmonyPatch(typeof(RcVehicle), "FixedUpdate")]
    public class PermaRearCam
    {
        static AccessTools.FieldRef<RcVehiclePhysic, StatsModifiers> statmod = AccessTools.FieldRefAccess<StatsModifiers>(typeof(RcVehiclePhysic), "statsModifiers");
        static void Postfix(RcVehicle __instance, RcVehiclePhysic ___m_pVehiclePhysic)
        {
            var tmp = statmod(___m_pVehiclePhysic).Driver.LocalHumanId;
            if (Plugin.run && tmp == 0 && FreeCam.friKamera)
            {
                __instance.AttachedCamera.GetComponent<CameraBase>().SwitchCamera(ECamState.Rear, ECamState.TransCut);
            }
            else if (Plugin.run && tmp == 0 )
            {
                __instance.AttachedCamera.GetComponent<CameraBase>().SwitchCamera(ECamState.Follow, ECamState.TransCut);
            }
            
        }
    }

    [HarmonyPatch(typeof(HUDMiniMapHD), "Update")]
    public class AlwaysShowNames
    {
        private static bool Prefix(ref bool ___m_showNickname)
        {
            ___m_showNickname = true;
            return true;
        }
    }


    public class FreeCam : MonoBehaviour
    {
        static AccessTools.FieldRef<CameraBase, Transform> camtransform = AccessTools.FieldRefAccess<Transform>(typeof(CameraBase), "m_pTransform");

        void Awake()
        {

        }

        private void Update()
        {
            bool flag = !Plugin.run && !this.running;
            if (!flag)
            {
                this.running = true;
                this.body.detectCollisions = false;
                this.body.useGravity = false;
                this.body.isKinematic = false;
                this.body.velocity = Vector3.zero;
                this.body.angularVelocity = Vector3.zero;
                this.driver.SetInvulnerabilityActive(true);
                bool flag2 = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                float d = flag2 ? this.fastMovementSpeed : this.movementSpeed;
                Vector3 vector = base.transform.position;
                if (this.drivers == null)
                {
                    this.drivers = new Driver[Singleton<GameManager>.Instance.GameMode.DriversCount];

                    if (GkNetMgr.Instance.IsDisconnectedOrMasterClient)
                    {
                        foreach (Driver drivur in Singleton<GameManager>.Instance.GameMode.Drivers.Values)
                        {
                            //FileLog.Log("ID: " + drivur.Id.ToString());
                            //FileLog.Log("LOCAL ID: " + drivur.LocalId.ToString());
                            this.drivers[drivur.LocalId] = drivur;
                        }
                    }
                    else 
                    {
                        foreach (Driver drivur in Singleton<GameManager>.Instance.GameMode.Drivers.Values)
                        {
                            //FileLog.Log("ID: " + drivur.Id.ToString());
                            //FileLog.Log("LOCAL ID: " + drivur.LocalId.ToString());
                            this.drivers[drivur.Id-1] = drivur;
                        }
                    }                                                        
                }
                /*if (!Input.anyKey)
                {
                    vector = this.updateLocalFollow(vector);
                    this.checkMouse();
                    if (following == -1 || drivers[following].LocalHumanId == 0)
                    {
                        this.body.transform.position = vector;
                    }
                    return;
                }*/
                
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    this.following = 0;
                }
                else
                {
                    if (Input.GetKeyDown(KeyCode.Alpha2))
                    {
                        //var mycam = this.driver.Kart.AttachedCamera;
                        //var theircam = drivers[1].Kart.AttachedCamera;
                        //camtransform(mycam.GetComponent<CameraBase>())  = drivers[1].gameObject.transform;
                        //this.driver.transform.parent = drivers[1].gameObject.transform;
                        //this.driver.Kart.AttachedCamera.GetComponent<CameraBase>().Driver = driver;
                        this.following = 1;
                    }
                    else
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha3))
                        {
                            this.following = 2;
                        }
                        else
                        {
                            if (Input.GetKeyDown(KeyCode.Alpha4))
                            {
                                this.following = 3;
                            }
                            else
                            {
                                if (Input.GetKeyDown(KeyCode.Alpha5))
                                {
                                    this.following = 4;
                                }
                                else
                                {
                                    if (Input.GetKeyDown(KeyCode.Alpha6))
                                    {
                                        this.following = 5;
                                    }
                                    else
                                    {
                                        if (Input.GetKeyDown(KeyCode.Alpha7))
                                        {
                                            this.following = 6;
                                        }
                                        else
                                        {
                                            if (Input.GetKeyDown(KeyCode.Alpha8))
                                            {
                                                this.following = 7;
                                            }
                                            else
                                            {
                                                if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Alpha0))
                                                {
                                                    this.following = -1;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }             
                if ((following >= 0 && drivers[following].LocalHumanId == 0) || following < 0) friKamera = true;
                else friKamera =  false;
                this.checkMouse();
                vector = this.updateLocalFollow(vector);


                
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                {
                    vector += -base.transform.right * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                {
                    vector += base.transform.right * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                {
                    Vector3 direction = base.transform.forward;
                    direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                    vector += direction * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
                {
                    Vector3 direction = base.transform.forward;
                    direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                    vector += -direction * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.Q))
                {
                    vector += base.transform.up * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.E))
                {
                    vector += -base.transform.up * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.Space))
                {
                    vector += Vector3.up * d * Time.deltaTime;
                }
                if (Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.PageDown))
                {
                    vector += -Vector3.up * d * Time.deltaTime;
                }
                if (this.looking)
                {
                    float y = this.body.rotation.eulerAngles.y + Input.GetAxisRaw("Mouse X") * this.freeLookSensitivity;
                    float x = this.body.rotation.eulerAngles.x - Input.GetAxisRaw("Mouse Y") * this.freeLookSensitivity;
                    Quaternion rotation = default(Quaternion);
                    rotation.eulerAngles = new Vector3(x, y, 0f);
                    this.body.rotation = rotation;
                }
                float axis = Input.GetAxis("Mouse ScrollWheel");
                if (axis != 0f)
                {
                    float d2 = flag2 ? this.fastZoomSensitivity : this.zoomSensitivity;
                    vector += base.transform.forward * axis * d2;
                }
                if (Input.GetKeyDown(KeyCode.Mouse1))
                {
                    this.StartLooking();
                }
                else
                {
                    if (Input.GetKeyUp(KeyCode.Mouse1))
                    {
                        this.StopLooking();
                    }
                }
                //FileLog.Log(vector.ToString());
                if (following == -1 || drivers[following].LocalHumanId == 0)
                {
                    this.body.transform.position = vector;
                }
               
                

            }


        }

        public Vector3 updateLocalFollow(Vector3 vector)
        {
            if (this.following > -1 && this.following < this.drivers.Length && GkNetMgr.Instance.IsDisconnectedOrMasterClient && drivers[following].IsAi)
            {
                //FileLog.Log("test");
                vector = this.drivers[this.following].transform.position;
                var rotat = this.drivers[this.following].transform.rotation;
                var velocity = this.drivers[this.following].Kart.GetLocalVelocity();
                this.body.MovePosition(Vector3.Lerp(this.body.position, vector, Time.deltaTime * 3.5f));
                this.body.rotation = Quaternion.Lerp(this.body.rotation, rotat, Time.deltaTime * 3.5f);
                this.body.velocity = Vector3.Lerp(this.body.velocity, velocity, Time.deltaTime * 3.5f);                
            }
            return vector;
        }

        public void checkMouse()
        {
            if (Input.GetKeyDown(KeyCode.Mouse0) && !freeCamLockOn)
            {
                Driver bestCandidate = null;
                float bestScore = 10000000f;
                for (int i = 0; i < drivers.Length; i++)
                {
                    //FileLog.Log(drivers[i].Id.ToString());
                    if (drivers[i].LocalHumanId == 0) continue;
                    Vector3 vec = drivers[i].gameObject.transform.position - driver.gameObject.transform.position;
                    float magnitud = vec.magnitude;
                    //FileLog.Log("Driver: " + i.ToString() + " : "  + magnitud.ToString());
                    vec = vec.normalized;
                    //FileLog.Log(vec.ToString());
                    Vector3 camvec = driver.gameObject.transform.forward;
                    //FileLog.Log(camvec.ToString());
                    float angle = Vector3.Angle(camvec, vec);
                    //FileLog.Log("Degrees: " + angle.ToString() + "\n");


                    if (angle < bestScore && magnitud < 100f)
                    {
                        bestScore = angle;
                        bestCandidate = drivers[i];
                    }
                }
                if (bestCandidate != null)
                {
                    freeCamLockOnTarget = bestCandidate;
                    freeCamLockOn = true;
                }
            }
            else if (Input.GetKeyDown(KeyCode.Mouse0) && freeCamLockOn)
            {
                freeCamLockOn = false;
                freeCamLockOnTarget = null;
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Mouse2) && !followLockOn)
                {
                    int bestCandidate = -1;
                    float bestScore = 10000000f;
                    for (int i = 0; i < drivers.Length; i++)
                    {
                        if (drivers[i].LocalHumanId == 0) continue;
                        Vector3 vec = drivers[i].gameObject.transform.position - driver.gameObject.transform.position;
                        float magnitud = vec.magnitude;
                        //FileLog.Log("Driver: " + i.ToString() + " : "  + magnitud.ToString());
                        vec = vec.normalized;
                        //FileLog.Log(vec.ToString());
                        Vector3 camvec = driver.gameObject.transform.forward;
                        //FileLog.Log(camvec.ToString());
                        float angle = Vector3.Angle(camvec, vec);
                        //FileLog.Log("Degrees: " + angle.ToString() + "\n");


                        if (angle < bestScore && magnitud < 100f)
                        {
                            bestScore = angle;
                            bestCandidate = i;
                        }
                    }
                    if (bestCandidate != -1)
                    {
                        this.following = bestCandidate;
                        followLockOn = true;
                        friKamera = false;
                    }
                }
                else if (Input.GetKeyDown(KeyCode.Mouse2) && followLockOn)
                {
                    followLockOn = false;
                    this.following = -1;
                    friKamera = true;
                }
            }
            if (freeCamLockOn && freeCamLockOnTarget != null)
            {
                var rotation = Quaternion.LookRotation(freeCamLockOnTarget.transform.position - driver.transform.position);
                driver.transform.rotation = Quaternion.Lerp(driver.transform.rotation, rotation, Time.deltaTime * 3f);
                //driver.transform.LookAt(freeCamLockOnTarget.transform, freeCamLockOnTarget.transform.forward);
            }
        }

        private void OnDisable()
        {
            this.StopLooking();
        }

        public void StartLooking()
        {
            this.looking = true;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        public void StopLooking()
        {
            this.looking = false;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public float movementSpeed = 45f;

        public Driver[] drivers;

        public float fastMovementSpeed = 75f;

        public Rigidbody body;

        public Driver driver;

        public int following = -1;

        public float freeLookSensitivity = 2f;

        public float zoomSensitivity = 10f;

        public float fastZoomSensitivity = 20f;

        private bool looking = false;

        private bool running = false;

        public static bool freeCamLockOn = false;

        public Driver freeCamLockOnTarget = null;

        public static bool followLockOn = false;

        public static bool friKamera = true;

    }
}
