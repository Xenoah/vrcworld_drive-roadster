import json,math,os
R=os.path.dirname(os.path.abspath(__file__));P=os.path.abspath(os.path.join(R,'../../Assets/DriveRoadster/Art/Course'));d=json.load(open(P+'/course_manifest.json'))
checks={}
checks['finite_geometry_and_no_zero_area_polygons']=not d['geometry_errors']
checks['ordered_gate_indices']=[c['index'] for c in d['checkpoints']]==list(range(10))
checks['finish_equals_start']=d['checkpoints'][0]['position']==d['checkpoints'][-1]['position']
checks['spawn_precedes_start']=d['spawn']['position'][2]<0
checks['gate_forward_up_orthogonal']=all(abs(sum(a*b for a,b in zip(c['forward'],c['up'])))<1e-8 for c in d['checkpoints'])
checks['visual_under_50000_triangles']=d['render_triangles']<50000
checks['collision_under_2000_triangles']=d['collision_triangles']<2000
checks['flight_gate_range']=d['flightStartGate']==3 and d['flightEndGate']==7
checks['units_metres']=d['units']=='metres'
report={'checks':checks,'passed':all(checks.values()),'render_triangles':d['render_triangles'],'collision_triangles':d['collision_triangles'],'nominal_route_length_m':round(280+2*math.pi*90+100+150+30,2),'max_bank_degrees':72,'horizontal_curve_radius_m':90,'track_width_m':18,'flight_clear_ring_diameter_m':21.6,'unity_import_and_playtest':'NOT RUN','limitations':['No Unity/VRChat build, physics drive, multiplayer or headset validation.','90–150 seconds is a design target; actual lap time has not been measured.','Static nonconvex MeshColliders required for ribbon surfaces; collision meshes must not render.','Flight path requires controlled descent for landing; flat unlimited glide cannot complete the intended course.']}
json.dump(report,open(R+'/geometry_validation.json','w'),indent=2);print(json.dumps(report,indent=2));assert report['passed']
