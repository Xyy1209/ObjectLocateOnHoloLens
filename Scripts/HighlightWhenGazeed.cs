using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HoloToolkit.Unity;
using HoloToolkit.Unity.InputModule;

public class HighlightWhenGazeed : MonoBehaviour,IFocusable
{
    


    void start()
    {
        this.gameObject.GetComponentInChildren<TextMesh>().fontSize = 48;
        this.gameObject.GetComponentInChildren<TextMesh>().color = Color.yellow;
    }


    public void OnFocusEnter()
    {
        this.gameObject.GetComponentInChildren<TextMesh>().fontSize = 70;
        this.gameObject.GetComponentInChildren<TextMesh>().color = Color.green;
    }



    public void OnFocusExit()
    {
        this.gameObject.GetComponentInChildren<TextMesh>().fontSize = 48;
        this.gameObject.GetComponentInChildren<TextMesh>().color = Color.yellow;
    }
 

    void Update()
    {
        if (ToVideoFrame.Instance.evaluating == true)
            this.gameObject.GetComponentInChildren<TextMesh>().color = Color.red;
    }

}
