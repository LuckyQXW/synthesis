using UnityEngine;
using System.Collections.Generic;
using ExceptionHandling;
using System;

//Data struct for packets sent to WPI server through Unity client
public class InputStatePacket
{
    public DIOModule[] dio = new DIOModule[2];
    public Encoders[] encoders = new Encoders[4];
    public AnalogValues[] ai = new AnalogValues[1];
    public Counter[] counter = new Counter[8];

    public InputStatePacket()
    {
        for (int i = 0; i < dio.Length; i++)
        {
            dio[i] = new DIOModule();
        }
        for (int i = 0; i < encoders.Length; i++)
        {
            encoders[i] = new Encoders();
        }
        for (int i = 0; i < ai.Length; i++)
        {
            ai[i] = new AnalogValues();
        }
        for (int i = 0; i < counter.Length; i++)
        {
            counter[i] = new Counter();
        }

    }

    public class DIOModule
    {
        public const int LENGTH = 4;
        public UInt32 digitalInput;
    }

    public class Encoders
    {
        public const int LENGTH = 4;
        public Int32 value;
    }

    public class AnalogValues
    {
        public const int LENGTH = 4 * (8);
        public Int32[] analogValues = new Int32[8];
    }

    public class Counter
    {
        public const int LENGTH = 4;
        public Int32 value;
    }
    public int Write(byte[] packet)
    {
        int head = 0;
        for (int i = 0; i < dio.Length; i++)
        {

            Buffer.BlockCopy(new UInt32[] { dio[i].digitalInput }, 0, packet, head, DIOModule.LENGTH);
            head += DIOModule.LENGTH;
        }
        for (int i = 0; i < encoders.Length; i++)
        {

            Buffer.BlockCopy(new Int32[] { encoders[i].value }, 0, packet, head, Encoders.LENGTH);
            head += Encoders.LENGTH;
        }
        for (int i = 0; i < ai.Length; i++)
        {


            Buffer.BlockCopy(ai[i].analogValues, 0, packet, head, AnalogValues.LENGTH);
            head += AnalogValues.LENGTH;

        }
        for (int i = 0; i < counter.Length; i++)
        {

            Buffer.BlockCopy(new Int32[] { counter[i].value }, 0, packet, head, Counter.LENGTH);
            head += Counter.LENGTH;
        }
        return head;
    }
}

public class DriveJoints : MonoBehaviour
{
    // A function to handle solenoids
    // We will have accurate velocity measures later, but for now, we need something that works.
    public static void SetSolenoid(UnityRigidNode node, bool forward)
    {
        // Acceleration of the piston, whose value will be determined by the following try/catch statement
        float acceleration = 0;


        // Checks to make sure solenoid data was assigned. We can't really use a try/catch statement because if pressure and diameter data is left blank when the robot is created, Unity will still use its default values.
        if (node.GetJoint<ConfigurableJoint>().xDrive.maximumForce < 3.4e36 || node.GetJoint<ConfigurableJoint>().xDrive.maximumForce <= 0 || node.GetJoint<ConfigurableJoint>().xDrive.maximumForce != null)
        {
            acceleration = node.GetJoint<ConfigurableJoint>().xDrive.maximumForce / node.GetJoint<ConfigurableJoint>().rigidbody.mass * (forward ? 1 : -1);
        }
        else
        {
            // Calculating an arbitrary maximum force. Assumes the piston diameter is .5 inches and that the PSI is 60psi. 
            float psiToNMm2 = 0.00689475728f;
            float maximumForce = (psiToNMm2 * 60f) * (Mathf.PI * Mathf.Pow(6.35f, 2f));
            acceleration = (maximumForce / node.GetJoint<ConfigurableJoint>().rigidbody.mass) * (forward ? 1 : -1);
            throw new PistonDataMissing(node.ToString());
        }

        // Dot product is reversed, so we need to negate it
        float velocity = acceleration * (Time.deltaTime) - Vector3.Dot(node.GetJoint<ConfigurableJoint>().rigidbody.velocity, node.unityObject.transform.TransformDirection(node.GetJoint<ConfigurableJoint>().axis));

        node.GetJoint<ConfigurableJoint>().targetVelocity = new Vector3(velocity, 0, 0);
    }

    // Rotates a wheel 45 degress to act as a mecanum wheel
    public static void RotateWheel45(List<UnityRigidNode> wheels)
    {
        foreach (UnityRigidNode wheel in wheels)
        {
            //wheel.GetWheelCollider ().transform.rotation = Quaternion.Euler(45, 90, 270);
            wheel.wCollider.GetComponent<WheelCollider>().transform.Rotate(0, -315, 0);
        }
    }

    // Gets the linear position of a UnityRigidNode relative to its parent (intended to be used with pistons, but it could be used elsewhere)
    public static float GetLinearPositionRelativeToParent(UnityRigidNode baseNode)
    {
        Vector3 baseDirection = baseNode.unityObject.transform.rotation * baseNode.GetJoint<Joint>().axis;
        baseDirection.Normalize();
        UnityRigidNode parentNode = (UnityRigidNode) (baseNode.GetParent());

        // Vector difference between the world positions of the node, and the parent of the node
        Vector3 difference = baseNode.unityObject.transform.position - parentNode.unityObject.transform.position;

        // Find the magnitude of 'difference' along the baseDirection
        float linearPositionAlongAxis = Vector3.Dot(baseDirection, difference);

        // The dot product we get is inverted, so we need to invert it again before we return it.
        return -linearPositionAlongAxis;
    }

    // Get Angle between two up vectors
    public static float GetAngleBetweenChildAndParent(UnityRigidNode child)
    {
        HingeJoint hinge = child.GetJoint<HingeJoint>();
        if (hinge != null)
        {
            return hinge.angle;
        }
        UnityRigidNode parent = (UnityRigidNode) child.GetParent();
        return (180f / Mathf.PI) * (Mathf.Acos(Vector3.Dot(child.unityObject.transform.up, parent.unityObject.transform.up) / (child.unityObject.transform.up.magnitude * parent.unityObject.transform.up.magnitude)));
    }

    // Drive All Motors Associated with a PWM port

    public static void UpdateAllMotors(RigidNode_Base skeleton, unityPacket.OutputStatePacket.DIOModule[] dioModules)
    {
        float[] pwm = dioModules[0].pwmValues;

        List<RigidNode_Base> listOfSubNodes = new List<RigidNode_Base>();
        skeleton.ListAllNodes(listOfSubNodes);

        // Cycles through the packet
        for (int i = 0; i < pwm.Length; i++)
        {
            foreach (RigidNode_Base node in listOfSubNodes)
            {
                // Typcasting RigidNode to UnityRigidNode to use UnityRigidNode functions
                UnityRigidNode unitySubNode = (UnityRigidNode) node;

                // Checking if there is a joint (and a joint driver) attatched to each joint
                if (unitySubNode.GetSkeletalJoint() != null && unitySubNode.GetSkeletalJoint().cDriver != null && unitySubNode.GetSkeletalJoint().cDriver.GetDriveType().IsMotor())
                {
                    // If port A matches the index of the array in the packet, (A.K.A: the packet index is reffering to the wheelCollider on the subNode0), then that specific wheel Collider is set.
                    if (unitySubNode.IsWheel && unitySubNode.GetSkeletalJoint().cDriver.portA == i + 1)
                    {
                        float OzInToNm = .00706155183333f;
                        BetterWheelCollider bwc = unitySubNode.unityObject.GetComponent<BetterWheelCollider>();
                        bwc.currentTorque = OzInToNm * (pwm[i] * 171.1f);
                        bwc.brakeTorque = 343f * OzInToNm;
                    }
                    else if (unitySubNode.GetSkeletalJoint().cDriver.portA == i + 1)
                    {
                        Joint joint = unitySubNode.GetJoint<Joint>();

                        // Something Arbitrary for now. 4 radians/second
                        float OzInToNm = .00706155183333f / 360.0f;
                        float motorForce = OzInToNm * (Math.Abs(pwm[i]) < 0.05f ? 343f : (pwm[i] * pwm[i] * 171.1f));
                        float targetVelocity = 10000f * Math.Sign(pwm[i]);
                        #region Config_Joint
                        if (joint != null && joint is ConfigurableJoint)
                        {
                            ConfigurableJoint cj = (ConfigurableJoint) joint;
                            JointDrive jD = cj.angularXDrive;
                            jD.maximumForce = motorForce;
                            cj.angularXDrive = jD;
                            cj.targetAngularVelocity = new Vector3(targetVelocity, 0, 0);

                            // We will need this to tell when the joint is very near a limit
                            float angularPosition = GetAngleBetweenChildAndParent(unitySubNode);

                            // Stopping the configurable joint if it approaches its limits (if its within 5% of its limit)
                            if (cj.angularXMotion == ConfigurableJointMotion.Limited
                                && (cj.highAngularXLimit.limit - angularPosition) <
                                (0.05f * cj.highAngularXLimit.limit))
                            {
                                // This prevents the motor from rotating toward its limit again after we have gotten close enough to the limit that we need to stop it.
                                // We will need it to be able to rotate away from the limit however (hence, the if-else statements)
                                // If the local up Vector of the unityObject is negative, the joint is approaching its positive limit (I am not sure if this will work in all cases, so its testing time!)
                                if (unitySubNode.unityObject.transform.up.x < 0 && cj.targetAngularVelocity.x > 0)
                                {
                                    cj.targetAngularVelocity = Vector3.zero;
                                }
                                else if (unitySubNode.unityObject.transform.up.x > 0 && cj.targetAngularVelocity.x < 0)
                                {
                                    cj.targetAngularVelocity = Vector3.zero;
                                }
                            }
                        }
                        #endregion
                        #region hinge joint
                        if (joint != null && joint is HingeJoint)
                        {
                            HingeJoint hj = (HingeJoint) joint;
                            JointMotor motor = hj.motor;
                            motor.force = motorForce;
                            motor.freeSpin = false;
                            motor.targetVelocity = targetVelocity;
                            if (hj.useLimits)
                            {
                                float limitRange = hj.limits.max - hj.limits.min;
                                if (Math.Min(Math.Abs(hj.angle - hj.limits.min), Math.Abs(hj.angle - hj.limits.max)) < 0.05 * limitRange)
                                {
                                    // This prevents the motor from rotating toward its limit again after we have gotten close enough to the limit that we need to stop it.
                                    // We will need it to be able to rotate away from the limit however (hence, the if-else statements)
                                    // If the local up Vector of the unityObject is negative, the joint is approaching its positive limit (I am not sure if this will work in all cases, so its testing time!)
                                    if (unitySubNode.unityObject.transform.up.x < 0 && motor.targetVelocity > 0)
                                    {
                                        motor.targetVelocity = 0;
                                    }
                                    else if (unitySubNode.unityObject.transform.up.x > 0 && motor.targetVelocity < 0)
                                    {
                                        motor.targetVelocity = 0;
                                    }
                                }
                            }
                            hj.motor = motor;
                        #endregion
                        }
                        else if (unitySubNode.GetSkeletalJoint().cDriver.portA == i + 1)
                        {
                            // Should we throw an exception here?
                            Debug.Log("There's an issue: We have an active motor not set (even though it should be set).");
                        }
                    }
                }
            }
        }
    }

    // This function takes a skeleton and byte (a packet) as input, and will use both to check if each solenoid port is open.

    public static void UpdateSolenoids(RigidNode_Base skeleton, unityPacket.OutputStatePacket.SolenoidModule[] solenoidModules)
    {
        byte packet = solenoidModules[0].state;

        List<RigidNode_Base> listOfNodes = new List<RigidNode_Base>();
        skeleton.ListAllNodes(listOfNodes);

        foreach (RigidNode_Base subBase in listOfNodes)
        {
            UnityRigidNode unityNode = (UnityRigidNode) subBase;
            Joint joint = unityNode.GetJoint<Joint>();
            // Make sure piston and skeletalJoint exist
            // If the rigidNodeBase contains a bumper_pneumatic joint driver (meaning that its a solenoid)
            if (joint != null && joint is ConfigurableJoint && subBase != null && subBase.GetSkeletalJoint() != null && subBase.GetSkeletalJoint().cDriver != null && (subBase.GetSkeletalJoint().cDriver.GetDriveType() == JointDriverType.BUMPER_PNEUMATIC || subBase.GetSkeletalJoint().cDriver.GetDriveType() == JointDriverType.RELAY_PNEUMATIC))
            {
                ConfigurableJoint cj = (ConfigurableJoint) joint;
                // It will use bitwise operators to check if the port is open (see wiki for full explanation).
                int stateA = packet & (1 << (subBase.GetSkeletalJoint().cDriver.portA - 1));
                int stateB = packet & (1 << (subBase.GetSkeletalJoint().cDriver.portB - 1));

                float linearPositionAlongAxis = GetLinearPositionRelativeToParent(unityNode);

                // Error catching is done in the SetSolenoid function
                if (stateA > 0 && stateB < 0)
                {
                    // Do Nothing. Both ports should not be open
                }
                else if (stateA < 0 && stateB < 0)
                {
                    // Again, do nothing. There will be no flow.
                }
                else if (stateA > 0)
                {
                    SetSolenoid(unityNode, true);
                }
                else if (stateB > 0)
                {
                    SetSolenoid(unityNode, false);
                }

                // If the piston hits its upper limit, stop it from extending any farther.
                if (Mathf.Abs(cj.linearLimit.limit - linearPositionAlongAxis) < (.03f * cj.linearLimit.limit))
                {
                    // Since we still want it to retract, however, we will only stop the piston if its velocity if positive. If its not, (its going backwards), we won't need to stop it
                    if (cj.targetVelocity.x > 0)
                    {
                        cj.targetVelocity = Vector3.zero;
                    }
                    // Otherwise, if the piston has reached its lower limit, we need to stop it from attempting retract farther.
                }
                else if (Mathf.Abs(-1 * cj.linearLimit.limit - linearPositionAlongAxis) < (.03f * cj.linearLimit.limit))
                {
                    if (cj.targetVelocity.x < 0)
                    {
                        cj.targetVelocity = Vector3.zero;
                    }
                }
            }
        }
    }

    //public static void UpdateAllJoints(RigidNode_Base skeleton, float[] pwmAssignments, byte solenoidAssignments) {
    //	UpdateAllWheels(skeleton, pwmAssignments);
    //	UpdateSolenoids(skeleton, solenoidAssignments);
    //}
}


