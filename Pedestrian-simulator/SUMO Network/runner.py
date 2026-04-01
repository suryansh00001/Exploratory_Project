#!/usr/bin/env python
# Eclipse SUMO, Simulation of Urban MObility; see https://eclipse.org/sumo
# Copyright (C) 2009-2019 German Aerospace Center (DLR) and others.
# This program and the accompanying materials
# are made available under the terms of the Eclipse Public License v2.0
# which accompanies this distribution, and is available at
# http://www.eclipse.org/legal/epl-v20.html
# SPDX-License-Identifier: EPL-2.0

# @file    runner.py
# @author  Lena Kalleske
# @author  Daniel Krajzewicz
# @author  Michael Behrisch
# @author  Jakob Erdmann
# @date    2009-03-26

"""
Tutorial for traffic light control via the TraCI interface.
This scenario models a pedestrian crossing which switches on demand.
"""
from __future__ import absolute_import
from __future__ import print_function

import os
import sys
import optparse
import subprocess
import time

import threading
import xml.etree.ElementTree as ET

from pynput.keyboard import Key, Listener

import json

velx = -1
vely = 0

# the directory in which this script resides
THISDIR = os.path.dirname(__file__)


# we need to import python modules from the $SUMO_HOME/tools directory
# If the the environment variable SUMO_HOME is not set, try to locate the python
# modules relative to this script
if 'SUMO_HOME' in os.environ:
    tools = os.path.join(os.environ['SUMO_HOME'], 'tools')
    sys.path.append(tools)
else:
    sys.exit("please declare environment variable 'SUMO_HOME'")
import traci  # noqa
from sumolib import checkBinary  # noqa
import randomTrips  # noqa


import time
import zmq

context = zmq.Context()
socket = context.socket(zmq.REP)
socket.bind("tcp://*:5555")
EGO_PED_ID = "ego_ped"
EGO_PED_LOG_PATH = os.path.join(THISDIR, "log.txt")
POSITION_BOUNDS = None
POSITION_BOUNDS_MARGIN = 2.0
ENABLE_VEHICLE_PEDESTRIAN_PROTECTION = True
VEHICLE_STOP_DISTANCE = 4.5
VEHICLE_SLOW_DISTANCE = 10.0
VEHICLE_SLOW_SPEED = 0.5
MANAGED_VEHICLE_SPEEDS = {}

class Object(dict):
  def __init__(self, name, x, y, angle, edge="", lane="", pedWalk="false", changedArea=False):
    dict.__init__(self, name = name, x = x, y = y, angle = angle, edge = edge, lane=lane, pedWalk=pedWalk, changedArea=changedArea) #pineapple

class Data:
    def __init__(self, persons, vehicles):
        self.persons = persons
        self.vehicles = vehicles

class PersonsData:
    def __init__(self, persons):
        self.persons = persons


def get_preferred_edge(person):
    edge = person.get("edge")
    if edge is None or edge == "":
        return "RDR"
    return edge


def is_person_known(person_name):
    if not person_name:
        return False

    try:
        traci.person.getRoadID(person_name)
        return True
    except traci.exceptions.TraCIException:
        return False


def ensure_person_exists(person):
    person_name = person.get("name")
    if not person_name:
        return False

    if is_person_known(person_name):
        return True

    edge = get_preferred_edge(person)
    try:
        traci.person.add(person_name, edge, 2)
        # Keep stages local to the current area to avoid disconnected-route aborts.
        traci.person.appendWalkingStage(person_name, [edge, edge], 1)
        return True
    except traci.exceptions.TraCIException:
        return False

def buildDataList(list, type):
    return_list = []
    for item in list:
        x = 0
        y = 0
        angle = 0
        if type == "vehicles":
            x, y = traci.vehicle.getPosition(item)
            angle = traci.vehicle.getAngle(item)
        else:
            x, y = traci.person.getPosition(item)
            angle = traci.person.getAngle(item)
        return_list.append(Object(item, x, y, angle))
    return return_list


def sumoDataToJSON():
    vehicles = buildDataList(traci.vehicle.getIDList(), "vehicles")
    persons = buildDataList(traci.person.getIDList(), "persons")
    #return json.dumps({"vehicles": dic_vehicles, "pedestrians": dic_persons})
    data = Data(persons, vehicles)
    return json.dumps(data.__dict__)


def parse_shape_bounds(shape_text, bounds):
    if not shape_text:
        return bounds

    min_x, min_y, max_x, max_y = bounds
    for pair in shape_text.split():
        if "," not in pair:
            continue
        try:
            x_str, y_str = pair.split(",", 1)
            x = float(x_str)
            y = float(y_str)
        except ValueError:
            continue
        min_x = min(min_x, x)
        min_y = min(min_y, y)
        max_x = max(max_x, x)
        max_y = max(max_y, y)

    return (min_x, min_y, max_x, max_y)


def load_world_bounds(net_file, margin=0.0):
    path = os.path.join(THISDIR, net_file)
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError):
        return None

    location = root.find("location")
    if location is None:
        return None

    conv_boundary = location.get("convBoundary")
    if not conv_boundary:
        return None

    try:
        min_x, min_y, max_x, max_y = [float(value) for value in conv_boundary.split(",")]
    except ValueError:
        return None

    if max_x <= min_x or max_y <= min_y:
        return None

    return (min_x - margin, min_y - margin, max_x + margin, max_y + margin)


def clamp_person_xy(person, bounds):
    x = float(person.get("x"))
    y = float(person.get("y"))

    if bounds is None:
        return x, y, False

    min_x, min_y, max_x, max_y = bounds
    clamped_x = min(max(x, min_x), max_x)
    clamped_y = min(max(y, min_y), max_y)
    return clamped_x, clamped_y, (clamped_x != x or clamped_y != y)


def log_ego_ped_state(person):
    if person.get("name") != EGO_PED_ID:
        return

    edge = person.get("edge") or ""
    angle = person.get("angle")
    x = person.get("x")
    y = person.get("y")
    timestamp = int(round(time.time() * 1000))
    log_line = "{0}, id={1}, x={2}, y={3}, angle={4}, edge={5}\n".format(timestamp, EGO_PED_ID, x, y, angle, edge)

    try:
        with open(EGO_PED_LOG_PATH, "a") as log_file:
            log_file.write(log_line)
    except OSError:
        pass


def enforce_vehicle_pedestrian_safety(person_positions=None):
    if not ENABLE_VEHICLE_PEDESTRIAN_PROTECTION:
        return

    try:
        vehicle_ids = list(traci.vehicle.getIDList())
    except traci.exceptions.TraCIException:
        return

    active_vehicle_set = set(vehicle_ids)
    for managed_vehicle in list(MANAGED_VEHICLE_SPEEDS.keys()):
        if managed_vehicle not in active_vehicle_set:
            MANAGED_VEHICLE_SPEEDS.pop(managed_vehicle, None)

    if person_positions is None:
        person_positions = []
        try:
            person_ids = list(traci.person.getIDList())
        except traci.exceptions.TraCIException:
            person_ids = []

        for person_id in person_ids:
            try:
                px, py = traci.person.getPosition(person_id)
                person_positions.append((px, py))
            except traci.exceptions.TraCIException:
                continue

    stop_distance_sq = VEHICLE_STOP_DISTANCE * VEHICLE_STOP_DISTANCE
    slow_distance_sq = VEHICLE_SLOW_DISTANCE * VEHICLE_SLOW_DISTANCE

    for vehicle_id in vehicle_ids:
        try:
            vx, vy = traci.vehicle.getPosition(vehicle_id)
        except traci.exceptions.TraCIException:
            continue

        nearest_distance_sq = float("inf")
        for px, py in person_positions:
            dx = vx - px
            dy = vy - py
            distance_sq = dx * dx + dy * dy
            if distance_sq < nearest_distance_sq:
                nearest_distance_sq = distance_sq

        target_speed = None
        if nearest_distance_sq <= stop_distance_sq:
            target_speed = 0.0
        elif nearest_distance_sq <= slow_distance_sq:
            target_speed = VEHICLE_SLOW_SPEED

        last_speed = MANAGED_VEHICLE_SPEEDS.get(vehicle_id)

        if target_speed is None:
            if last_speed is not None:
                try:
                    traci.vehicle.setSpeed(vehicle_id, -1)
                except traci.exceptions.TraCIException:
                    pass
                MANAGED_VEHICLE_SPEEDS.pop(vehicle_id, None)
            continue

        if last_speed is None or abs(last_speed - target_speed) > 0.01:
            try:
                traci.vehicle.setSpeed(vehicle_id, target_speed)
                MANAGED_VEHICLE_SPEEDS[vehicle_id] = target_speed
            except traci.exceptions.TraCIException:
                continue


def handleComms(last_subject):
    message = socket.recv()
    #print("here?")
    #print(message)

    
    data = json.loads(message)
    #print(data)

    addsimstep = False
    persons = data.get("persons") or []

    for person in persons:
        if is_person_known(person.get("name")):
            continue
        if ensure_person_exists(person):
            addsimstep = True

    person_list = list(traci.person.getIDList())

    for person in persons:
        if person.get("name") in person_list: person_list.remove(person.get("name"))

    #for person_name in person_list:
        #print (person_list)
        #traci.person.removeStages(person_name)

    # Route stage resets are disabled for externally controlled pedestrians.
    # Continuous moveToXY updates are more stable for intersection/crosswalk traversal.

    if addsimstep:
       traci.simulationStep()

    moved_person_positions = []

    for person in persons:
        if not ensure_person_exists(person):
            continue

        try:
            person_id = person.get("name")
            edge_hint = person.get("edge") or ""
            safe_x, safe_y, clamped = clamp_person_xy(person, POSITION_BOUNDS)
            angle = person.get("angle")
            try:
                traci.person.moveToXY(person_id, edge_hint, safe_x, safe_y, angle=angle, keepRoute=3)
            except traci.exceptions.TraCIException:
                traci.person.moveToXY(person_id, "", safe_x, safe_y, angle=angle, keepRoute=2)
            moved_person_positions.append((safe_x, safe_y))
        except traci.exceptions.TraCIException:
            # A person can disappear between checks due to routing/stage updates.
            # Ignore this frame and continue to keep the bridge alive.
            continue

        if clamped and person.get("name") == EGO_PED_ID:
            print("Clamped ego_ped to bounds at x={0:.2f}, y={1:.2f}".format(safe_x, safe_y))

        log_ego_ped_state(person)

    enforce_vehicle_pedestrian_safety(moved_person_positions)

    '''
    if last_subject.get("edge") != subject.get("edge") and subject.get("edge") != "":
        traci.person.removeStages("subject")
        traci.simulationStep()
        traci.simulationStep()
        traci.person.add("subject", "RDR", 2)
        traci.person.appendWalkingStage("subject", [subject.get("edge"), "RDR"], 1)
        traci.simulationStep()
        traci.simulationStep()

    #Working with keepRpute = 3 and edge = ""
    #traci.person.moveToXY("subject", "", subject.get("x"), subject.get("y"), angle=subject.get("angle"), keepRoute=3)
    traci.person.moveToXY("subject", "", subject.get("x"), subject.get("y"), angle=subject.get("angle"), keepRoute=3)
    '''

    send = sumoDataToJSON()
    socket.send_string(send)

    #return subject


def run():
    global velx
    global vely
    mili = 0
    controlled_person_id = EGO_PED_ID

    traci.simulationStep()
    if not is_person_known(controlled_person_id):
        traci.person.add(controlled_person_id, "RDR", 2)
        traci.person.appendWalkingStage(controlled_person_id, ["RDR", "RDR"], 1)
    traci.simulationStep()

    x, y = traci.person.getPosition(controlled_person_id)
    angle = traci.person.getAngle(controlled_person_id)
    edge = traci.person.getRoadID(controlled_person_id)
    last_subject = Object(controlled_person_id, x, y, angle, edge)

    while traci.simulation.getMinExpectedNumber() > 0:
        if int(round(time.time() * 1000)) - mili > 11:
            try:
                #if velx != 0 or vely != 0:
                    #traci.person.moveToXY("subject", "", traci.person.getPosition("subject")[0] + (velx / 100), traci.person.getPosition("subject")[1] + (vely / 100), keepRoute=3)
                #print("Received request: %s" % data_receive)
                new_subject = handleComms(last_subject)
                mili = int(round(time.time() * 1000))
                last_subject = new_subject
                traci.simulationStep()
                enforce_vehicle_pedestrian_safety()
            except traci.exceptions.FatalTraCIError as ex:
                print("Fatal TraCI error, stopping bridge: {0}".format(ex))
                break
        

        

def get_options():
    """define options for this script and interpret the command line"""
    optParser = optparse.OptionParser()
    optParser.add_option("--nogui", action="store_true",
                         default=False, help="run the commandline version of sumo")
    options, args = optParser.parse_args()
    return options


def on_press(key):
    global vely
    global velx
    key_press = key
    #print("PRESSED", key_press)
    if key_press == Key.esc:
        return False
    if key_press == Key.right:
        vely = 0
        velx = 1
    if key_press == Key.left:
        vely = 0
        velx = -1
    if key_press == Key.up:
        vely = 1
        velx = 0
    if key_press == Key.down:
        vely = -1
        velx = 0


def run_listener():
    with Listener(on_press=on_press) as listener:
        listener.join()

# this is the main entry point of this script
if __name__ == "__main__":

    os.chdir(os.path.dirname(sys.argv[0]))
    # load whether to run with or without GUI
    options = get_options()

    # this script has been called from the command line. It will start sumo as a
    # server, then connect and run
    if options.nogui:
        sumoBinary = checkBinary('sumo')
    else:
        sumoBinary = checkBinary('sumo-gui')

    net = 'custom.net.xml'
    POSITION_BOUNDS = load_world_bounds(net, margin=POSITION_BOUNDS_MARGIN)
    if POSITION_BOUNDS is not None:
        min_x, min_y, max_x, max_y = POSITION_BOUNDS
        print("Global pedestrian bounds active: x=[{0:.2f}, {1:.2f}], y=[{2:.2f}, {3:.2f}]".format(min_x, max_x, min_y, max_y))
    else:
        print("Global pedestrian bounds unavailable; using raw coordinates.")
    # build the multi-modal network from plain xml inputs
    """subprocess.call([checkBinary('netconvert'),
                     #'-c', os.path.join('data', 'pedcrossing.netccfg'),
                     '--output-file', net],
                    stdout=sys.stdout, stderr=sys.stderr)
    """

    """
    # generate the pedestrians for this simulation
    randomTrips.main(randomTrips.get_options([
        '--net-file', net,
        '--output-trip-file', 'pedestrians.trip.xml',
        '--seed', '42',  # make runs reproducible
        '--pedestrians',
        '--prefix', 'ped',
        # prevent trips that start and end on the same edge
        '--min-distance', '1',
        '--trip-attributes', 'departPos="random" arrivalPos="random"',
        '--binomial', '4',
        '--period', '3']))
    """

    #thread2 = threading.Thread(target=run_listener, args=())
    #thread2.start()

    # this is the normal way of using traci. sumo is started as a
    # subprocess and then the python script connects and runs
    traci.start([
        sumoBinary,
        '-c', 'run.sumocfg',
        '--step-length', '0.011',
        '--ignore-route-errors', 'true'
    ])

    try:
        run()
    finally:
        try:
            traci.close(False)
        except traci.exceptions.TraCIException:
            pass
