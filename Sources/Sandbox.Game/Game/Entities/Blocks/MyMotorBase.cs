﻿using Havok;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine.Models;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game.GUI;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.EntityComponents;
using VRage;
using VRage.Audio;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using System.Text;
using Sandbox.Game.Localization;
using Sandbox.Engine.Multiplayer;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Network;
using Sandbox.Game.Replication;
using Sandbox.Game.Entities.Blocks;
using VRage.Sync;

namespace Sandbox.Game.Entities.Cube
{
    public abstract partial class MyMotorBase : MyMechanicalConnectionBlockBase
    {
        private const string ROTOR_DUMMY_KEY = "electric_motor";

        private static List<HkBodyCollision> m_penetrations = new List<HkBodyCollision>();

        private Vector3 m_dummyPos;

#if XB1 // XB1_SYNC_NOREFLECTION
        protected /*readonly*/ Sync<float> m_dummyDisplacement;
#else // !XB1
        protected readonly Sync<float> m_dummyDisplacement;
#endif // !XB1
        protected event Action<MyMotorBase> AttachedEntityChanged;


        public Vector3 DummyPosition
        {
            get
            {
                var displacementVector = Vector3.Zero;
                if (m_dummyPos.Length() > 0)
                {
                    displacementVector = Vector3.DominantAxisProjection(m_dummyPos);
                    displacementVector.Normalize();
                    displacementVector *= m_dummyDisplacement;
                }
                else
                {
                    displacementVector = new Vector3(0, m_dummyDisplacement, 0);
                }
                return Vector3.Transform(m_dummyPos + displacementVector, this.PositionComp.LocalMatrix);
            }
        }

        public float DummyDisplacement
        {
            get { return m_dummyDisplacement + ModelDummyDisplacement; }
            set { m_dummyDisplacement.Value = value - ModelDummyDisplacement; }
        }

        public MyCubeGrid RotorGrid { get { return m_topGrid; } }

        public MyCubeBlock Rotor { get { return m_topBlock; } }

        public float RequiredPowerInput { get { return MotorDefinition.RequiredPowerInput; } }

        protected MyMotorStatorDefinition MotorDefinition { get { return (MyMotorStatorDefinition)BlockDefinition; } }

        protected virtual float ModelDummyDisplacement { get { return 0.0f; } }

        public Vector3 RotorAngularVelocity
        {
            // TODO: Imho it would be better to read velocity from constraint
            get { return CubeGrid.Physics.RigidBody.AngularVelocity - m_topGrid.Physics.RigidBody.AngularVelocity; }
        }

        public float MaxRotorAngularVelocity
        {
            get { return MyGridPhysics.GetShipMaxAngularVelocity(CubeGrid.GridSizeEnum); }
        }

   
        protected override bool CheckIsWorking()
        {
            return ResourceSink.IsPowered && base.CheckIsWorking();
        }

        public override void Init(MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            SyncFlag = true;

            var sinkComp = new MyResourceSinkComponent();
            sinkComp.Init(
                MotorDefinition.ResourceSinkGroup,
                MotorDefinition.RequiredPowerInput,
                () => (Enabled && IsFunctional) ? sinkComp.MaxRequiredInput : 0.0f);
            sinkComp.IsPoweredChanged += Receiver_IsPoweredChanged;
            ResourceSink = sinkComp;

            base.Init(objectBuilder, cubeGrid);

            SlimBlock.ComponentStack.IsFunctionalChanged += ComponentStack_IsFunctionalChanged;
         
            ResourceSink.Update();

            m_dummyDisplacement.Value = 0.0f;
            LoadDummyPosition();

            var ob = objectBuilder as MyObjectBuilder_MotorBase;

            //Backward compatibility 1.8.2016
            if (ob.RotorEntityId.HasValue && ob.RotorEntityId.Value != 0)
            {
                MyDeltaTransform? deltaTransform = ob.MasterToSlaveTransform.HasValue ? ob.MasterToSlaveTransform.Value : (MyDeltaTransform?)null;
                m_connectionState.Value = new State() { TopBlockId = ob.RotorEntityId, MasterToSlave = deltaTransform, Welded = ob.WeldedEntityId.HasValue || ob.ForceWeld };
            }

            AddDebugRenderComponent(new Components.MyDebugRenderComponentMotorBase(this));
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }
        
        private void Receiver_IsPoweredChanged()
        {
            UpdateIsWorking();
        }

        private void ComponentStack_IsFunctionalChanged()
        {
            ResourceSink.Update();
        }

        protected override void OnEnabledChanged()
        {
            ResourceSink.Update();
            base.OnEnabledChanged();
        }

        private void LoadDummyPosition()
        {
            var model = VRage.Game.Models.MyModels.GetModelOnlyDummies(BlockDefinition.Model);

            foreach (var dummy in model.Dummies)
            {
                if (dummy.Key.StartsWith(ROTOR_DUMMY_KEY, StringComparison.InvariantCultureIgnoreCase))
                {
                    var matrix = Matrix.Normalize(dummy.Value.Matrix);
                    m_dummyPos = matrix.Translation;
                    break;
                }
            }
        }

        public override void OnBuildSuccess(long builtBy)
        {
            Debug.Assert(m_constraint == null);

            if (Sync.IsServer)
            {
                CreateTopGrid(builtBy);
            }

            NeedsUpdate &= ~MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            base.OnBuildSuccess(builtBy);
        }

        public void RecreateRotor(long? builderId = null)
        {
            if (builderId.HasValue)
            {
                MyMultiplayer.RaiseEvent(this, x => x.DoRecreateRotor, builderId.Value);
            }
            else
            {
                MyMultiplayer.RaiseEvent(this, x => x.DoRecreateRotor, MySession.Static.LocalPlayerId);
            }
        }

        [Event, Reliable, Server]
        private void DoRecreateRotor(long builderId)
        {
            if (m_topBlock != null) return;

            CreateTopGrid(builderId);
        }

        protected override MatrixD GetTopGridMatrix()
        {
            return MatrixD.CreateWorld(Vector3D.Transform(DummyPosition, CubeGrid.WorldMatrix), WorldMatrix.Forward, WorldMatrix.Up);
        }

        protected override bool CanPlaceRotor(MyAttachableTopBlockBase rotorBlock, long builtBy)
        {
            return true;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            TryWeld();
            TryAttach();


            if (AttachedEntityChanged != null)
            {
                // OtherEntityId is null when detached, 0 when ready to connect, and other when attached
                AttachedEntityChanged(this);
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();
            TryWeld();
            TryAttach();
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();
            UpdateSoundState();
        }

        public override void ComputeTopQueryBox(out Vector3D pos, out Vector3 halfExtents, out Quaternion orientation)
        {
            var world = this.WorldMatrix;
            orientation = Quaternion.CreateFromRotationMatrix(world);
            halfExtents = Vector3.One * CubeGrid.GridSize * 0.35f;
            halfExtents.Y = CubeGrid.GridSize * 0.25f;
            pos = world.Translation + 0.35f * CubeGrid.GridSize * WorldMatrix.Up;
        }

        protected virtual void UpdateSoundState()
        {
            if (!MySandboxGame.IsGameReady || m_soundEmitter == null || IsWorking == false)
                return;

            if (m_topGrid == null || m_topGrid.Physics == null)
            {
                m_soundEmitter.StopSound(true);
                return;
            }

            if (IsWorking && Math.Abs(m_topGrid.Physics.RigidBody.DeltaAngle.W) > 0.00025f)
                m_soundEmitter.PlaySingleSound(BlockDefinition.PrimarySound, true);
            else
                m_soundEmitter.StopSound(false);

            if ((m_soundEmitter.Sound != null) && (m_soundEmitter.Sound.IsPlaying))
            {
                float semitones = 4f * (Math.Abs(RotorAngularVelocity.Length()) - 0.5f * MaxRotorAngularVelocity) / MaxRotorAngularVelocity;
                m_soundEmitter.Sound.FrequencyRatio = MyAudio.Static.SemitonesToFrequencyRatio(semitones);
            }
        }

        protected override Vector3D TransformPosition(ref Vector3D position)
        {
            return Vector3D.Transform(DummyPosition, CubeGrid.WorldMatrix);
        }

        protected override void DisposeConstraint()
        {
            if (m_constraint != null)
            {
                CubeGrid.Physics.RemoveConstraint(m_constraint);
                m_constraint.Dispose();
                m_constraint = null;
            }
        }
   }
}
