#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;

public static class RoadsterCockpitSetup
{
    static readonly Color Ice=new Color(.76f,.91f,.97f), Muted=new Color(.4f,.58f,.66f), Cyan=new Color(.28f,.85f,.96f);
    public static void Build(RoadsterVehicle vehicle, RoadsterRun run, GameObject[] models)
    {
        Transform car=vehicle.transform;
        vehicle.cockpitEye=New("CockpitEye_AuthoredReference",car,new Vector3(-.395f,1.08f,-.08f),Quaternion.identity);
        vehicle.yokes=new Transform[3]; vehicle.throttlePedals=new Transform[3]; vehicle.brakePedals=new Transform[3];
        for(int i=0;i<3;i++)
        {
            vehicle.yokes[i]=Pivot(car,models[i].transform,"SteeringWheel",i);
            vehicle.throttlePedals[i]=Pivot(car,models[i].transform,"Pedal_Throttle",i);
            vehicle.brakePedals[i]=Pivot(car,models[i].transform,"Pedal_Brake",i);
        }
        var main=Panel("DriverInstruments",car,new Vector3(-.35f,.94f,.604f),Quaternion.identity,new Vector2(1080,380),.00052f);
        TextAt(main,"Brand","ASTERION   /   DRIVER",-500,156,1000,42,27,Muted);
        run.speedText=TextAt(main,"Speed","0",-495,24,340,150,130,Ice);
        TextAt(main,"Units","km/h",-140,40,180,50,30,Muted);
        run.modeText=TextAt(main,"Mode","PARKED",105,82,415,65,42,Cyan);
        run.driveText=TextAt(main,"Input","DRIVE  0%",105,17,415,50,31,Ice);
        run.lapText=TextAt(main,"Timing","FREE DRIVE",-495,-111,1000,96,34,Ice);
        var system=Panel("FlightSystems",car,new Vector3(.34f,.94f,.594f),Quaternion.identity,new Vector2(980,380),.00054f);
        TextAt(system,"Title","FLIGHT SYSTEMS / LIVE",-450,155,900,40,27,Muted);
        run.systemText=TextAt(system,"State","STANDBY",-450,77,690,88,30,Ice);
        run.attitudeText=TextAt(system,"Attitude","PITCH 0 / ROLL 0",-450,-8,750,78,28,Ice);
        var horizon=ImageAt(system,"HorizonWindow",new Vector2(350,70),new Vector2(160,95),new Color(.025f,.07f,.1f));
        run.attitudeLine=ImageAt(horizon.transform,"Horizon",Vector2.zero,new Vector2(135,5),Ice).rectTransform;
        run.thrusterBars=new Image[7]; string[] labels={"FL","FR","RL","RR","L","R","PUSH"};
        for(int i=0;i<7;i++)
        {
            float x=-425+i*132;
            ImageAt(system,"JetSlot_"+i,new Vector2(x,-100),new Vector2(75,64),new Color(.035f,.07f,.085f));
            var bar=ImageAt(system,"Jet_"+i,new Vector2(x,-132),new Vector2(75,64),Cyan);
            bar.rectTransform.pivot=new Vector2(.5f,0);run.thrusterBars[i]=bar;
            TextAt(system,"JetLabel_"+i,labels[i],x-38,-158,90,32,22,Muted);
        }
        Vector3 controlsPosition=new Vector3(.12f,.765f,.10f);
        Quaternion facing=Quaternion.LookRotation(-(vehicle.cockpitEye.localPosition-controlsPosition).normalized,Vector3.up);
        var controls=Panel("DriverControls",car,controlsPosition,facing,new Vector2(800,850),.00034f);
        TextAt(controls,"Title","DRIVE CONTROLS",-365,385,730,48,34,Cyan);
        run.feedbackText=TextAt(controls,"Feedback","READY",-365,290,730,120,27,Ice);
        Button(controls,new Vector2(-185,145),new Vector2(330,105),"TIME ATTACK [T]",1,vehicle,run,false);
        Button(controls,new Vector2(185,145),new Vector2(330,105),"RESET [R]",0,vehicle,run,false);
        run.profileText=Button(controls,new Vector2(-185,5),new Vector2(330,105),"STEER / NORMAL [C]",3,vehicle,run,false);
        Button(controls,new Vector2(185,5),new Vector2(330,105),"CONTROLS [H]",4,vehicle,run,false);
        Button(controls,new Vector2(0,-135),new Vector2(700,105),"EXIT TO PADDOCK / JUMP / X",2,vehicle,run,true);
        run.helpText=TextAt(controls,"Help","",-365,-305,730,205,25,Muted);
        var door=Panel("BoardingStatus",car,new Vector3(-1.10f,1.02f,-.12f),Quaternion.Euler(0,90,0),new Vector2(620,240),.00060f);
        vehicle.boardingDisplay=TextAt(door,"Status","DRIVER ACCESS",-285,0,570,220,28,Ice);
        var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="BoardDriverSeat";board.transform.SetParent(door,false);
        board.transform.localPosition=new Vector3(0,0,18);board.transform.localScale=new Vector3(620,240,20);board.layer=8;
        board.GetComponent<Renderer>().enabled=false;board.GetComponent<Collider>().isTrigger=true;
        var boardAction=board.AddUdonSharpComponent<RoadsterAction>();boardAction.vehicle=vehicle;boardAction.run=run;boardAction.action=5;boardAction.InteractionText="Request driver seat";
        vehicle.InteractionText="Request driver seat";
        // Local visual style; these controls never modify the vehicle's flight safety tuning.
        var leather=AssetDatabase.LoadAssetAtPath<Material>("Assets/DriveRoadster/Art/GeneratedMaterials/DR_IvoryMicrofiber.mat");
        if(leather!=null) { leather.color=new Color(.78f,.80f,.81f);leather.SetFloat("_Metallic",0);leather.SetFloat("_Glossiness",.23f);EditorUtility.SetDirty(leather); }
    }
    static Transform Pivot(Transform car,Transform model,string name,int level)
    {
        Transform source=null;
        foreach(var t in model.GetComponentsInChildren<Transform>()) if(t.name==name) {source=t;break;}
        if(source==null)throw new InvalidOperationException("Missing cockpit pivot: "+name);
        var pivot=New("CockpitPivot_LOD"+level+"_"+name,car,car.InverseTransformPoint(source.position),Quaternion.identity);
        source.SetParent(pivot,true);return pivot;
    }
    static Transform Panel(string name,Transform parent,Vector3 position,Quaternion rotation,Vector2 size,float scale)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Canvas));go.layer=17;
        var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.localPosition=position;rect.localRotation=rotation;rect.localScale=Vector3.one*scale;rect.sizeDelta=size;
        go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        ImageAt(rect,"Backing",Vector2.zero,size,new Color(.008f,.016f,.022f,1));
        ImageAt(rect,"Accent",new Vector2(0,size.y*.5f-4),new Vector2(size.x,6),Cyan);
        return rect;
    }
    static Image ImageAt(Transform parent,string name,Vector2 position,Vector2 size,Color colour)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.layer=17;
        var image=go.GetComponent<Image>();image.color=colour;image.raycastTarget=false;
        var rect=image.rectTransform;rect.SetParent(parent,false);rect.anchoredPosition=position;rect.sizeDelta=size;return image;
    }
    static Text TextAt(Transform parent,string name,string content,float x,float y,float width,float height,int font,Color colour)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.layer=17;
        var text=go.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=font;text.color=colour;text.text=content;text.raycastTarget=false;
        text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
        var rect=text.rectTransform;rect.SetParent(parent,false);rect.pivot=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);
        return text;
    }
    static Text Button(Transform root,Vector2 position,Vector2 size,string label,int action,RoadsterVehicle vehicle,RoadsterRun run,bool exit)
    {
        var go=new GameObject("Control_"+action);go.transform.SetParent(root,false);go.transform.localPosition=new Vector3(position.x,position.y,-20);go.layer=8;
        var collider=go.AddComponent<BoxCollider>();collider.size=new Vector3(size.x,size.y,32);collider.isTrigger=true;
        var face=ImageAt(go.transform,"Cap",Vector2.zero,size,exit?new Color(.3f,.08f,.055f):new Color(.035f,.10f,.13f));
        var text=TextAt(face.transform,"Label",label,-size.x*.5f+16,0,size.x-32,size.y-8,27,exit?new Color(1f,.8f,.55f):Ice);
        text.transform.localPosition+=Vector3.back;
        var script=go.AddUdonSharpComponent<RoadsterAction>();script.vehicle=vehicle;script.run=run;script.action=action;script.visualCap=face.transform;script.InteractionText=label;
        return text;
    }
    static Transform New(string name,Transform parent,Vector3 position,Quaternion rotation)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;t.localRotation=rotation;return t;}
}
#endif
