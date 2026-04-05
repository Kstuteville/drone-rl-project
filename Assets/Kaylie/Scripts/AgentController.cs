using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;



public class AgentController : Agent
{
    [SerializeField] private Transform target;

    public override void OnEpisodeBegin()
    {
        // Reset the agent and target positions
        transform.localPosition = new Vector3(0f, 0.3f, 0f);
        int rand = Random.Range(0, 2);
        if (rand == 0)
        {
            target.localPosition = new Vector3(-3f, 0.3f, 0f);
        }
        if (rand == 1)
        {
            target.localPosition = new Vector3(3f, 0.3f, 0f);

        }
    }
    public override void CollectObservations(VectorSensor sensor)
    {
        // Implement your observation logic here
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(target.localPosition);
    }
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Implement your action logic here
        float move =actions.ContinuousActions[0];
        float moveSpeed = 2f;
         
         transform.localPosition += new Vector3(move,0f) * Time.deltaTime * moveSpeed;
    } 

public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
    continuousActions[0] = Input.GetAxisRaw("Horizontal");
    }

    
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Target")
        {
           AddReward(2f);
           EndEpisode();
        }
        if (other.gameObject.tag == "Wall")
        {
            AddReward(-1f);
            EndEpisode();
        }
    }

}