#!/usr/bin/env python3
"""
Phase 4 WebRTC Signaling Server
WebSocket 8766: Unity(browser) <-> Browser SDP/ICE relay
"""

import asyncio
import json
import logging
import argparse
from dataclasses import dataclass, field
from typing import Dict, Optional, Set
from websockets.asyncio.server import serve, ServerConnection
from websockets.exceptions import ConnectionClosed

logging.basicConfig(level=logging.INFO, format='%(asctime)s %(levelname)s %(message)s')
log = logging.getLogger("signaling")

# Room management
@dataclass
class Peer:
    ws: ServerConnection
    role: str  # "unity" or "browser"
    offer: Optional[str] = None
    candidates: list = field(default_factory=list)

@dataclass
class Room:
    peers: Dict[str, Peer] = field(default_factory=dict)  # role -> Peer
    pending_offer: Optional[str] = None
    pending_candidates: list = field(default_factory=list)

rooms: Dict[str, Room] = {}

def get_room(room_id: str) -> Room:
    if room_id not in rooms:
        rooms[room_id] = Room()
    return rooms[room_id]

def remove_room_if_empty(room_id: str):
    room = rooms.get(room_id)
    if room and not room.peers:
        del rooms[room_id]

async def handle_connection(ws: ServerConnection):
    path = ws.request.path
    # Parse query params: /?room=gallery&role=unity
    room_id = "gallery"
    role = None
    if "?" in path:
        params = path.split("?", 1)[1]
        for param in params.split("&"):
            if param.startswith("room="):
                room_id = param.split("=", 1)[1]
            elif param.startswith("role="):
                role = param.split("=", 1)[1]
    
    if role not in ("unity", "browser"):
        await ws.close(4002, "Explicit role required: unity or browser")
        return
    room = get_room(room_id)

    if role in room.peers:
        log.warning(f"Role {role} already taken in room {room_id}")
        await ws.close(4001, f"Role {role} already taken")
        return
    
    peer = Peer(ws=ws, role=role)
    room.peers[role] = peer
    log.info(f"[{room_id}] {role} connected from {ws.remote_address}")
    
    # Notify other peer
    other_role = "browser" if role == "unity" else "unity"
    if other_role in room.peers:
        try:
            await room.peers[other_role].ws.send(json.dumps({"type": "peer", "role": role}))
        except Exception as e:
            log.warning(f"Notify peer failed: {e}")
    
    # Flush pending offer/candidates to new peer
    if room.pending_offer and role == "unity":
        try:
            await ws.send(json.dumps({"type": "offer", "sdp": room.pending_offer}))
            log.info(f"[{room_id}] Flushed pending offer to {role}")
        except Exception as e:
            log.warning(f"Flush offer failed: {e}")
    
    for c in room.pending_candidates:
        if role == "unity":
            try:
                await ws.send(json.dumps(c))
            except Exception as e:
                log.warning(f"Flush candidate failed: {e}")
    
    try:
        async for message in ws:
            await handle_message(room_id, role, message)
    except ConnectionClosed:
        pass
    except Exception as e:
        log.error(f"[{room_id}] {role} error: {e}")
    finally:
        # Cleanup
        if room_id in rooms and role in rooms[room_id].peers:
            del rooms[room_id].peers[role]
            log.info(f"[{room_id}] {role} disconnected")
            
            # Notify other peer
            other_role = "browser" if role == "unity" else "unity"
            if other_role in room.peers:
                try:
                    await room.peers[other_role].ws.send(json.dumps({"type": "peer_left", "role": role}))
                except Exception:
                    pass
        
        room.pending_offer = None
        room.pending_candidates.clear()
        remove_room_if_empty(room_id)

async def handle_message(room_id: str, sender_role: str, message: str):
    room = get_room(room_id)
    try:
        msg = json.loads(message)
    except json.JSONDecodeError:
        return
    
    msg_type = msg.get("type")
    
    if msg_type == "offer":
        if sender_role != "browser":
            log.warning(f"[{room_id}] Offer from non-browser: {sender_role}")
            return
        room.pending_offer = msg.get("sdp")
        if not isinstance(room.pending_offer, str) or not room.pending_offer:
            return
        log.info(f"[{room_id}] Received offer from browser, length={len(room.pending_offer)}")
        
        # Forward to browser if connected
        if "unity" in room.peers:
            try:
                await room.peers["unity"].ws.send(json.dumps({"type": "offer", "sdp": room.pending_offer}))
                log.info(f"[{room_id}] Forwarded offer to unity")
            except Exception as e:
                log.warning(f"Forward offer failed: {e}")
    
    elif msg_type == "answer":
        if sender_role != "unity":
            log.warning(f"[{room_id}] Answer from non-unity: {sender_role}")
            return
        sdp = msg.get("sdp")
        if not isinstance(sdp, str) or not sdp:
            return
        log.info(f"[{room_id}] Received answer from unity, length={len(sdp)}")
        
        if "browser" in room.peers:
            try:
                await room.peers["browser"].ws.send(json.dumps({"type": "answer", "sdp": sdp}))
                log.info(f"[{room_id}] Forwarded answer to browser")
            except Exception as e:
                log.warning(f"Forward answer failed: {e}")
    
    elif msg_type == "candidate":
        candidate_msg = {
            "type": "candidate",
            "candidate": msg.get("candidate"),
            "sdpMid": msg.get("sdpMid"),
            "sdpMLineIndex": msg.get("sdpMLineIndex", 0)
        }
        
        if sender_role == "unity":
            # Forward to browser
            if "browser" in room.peers:
                try:
                    await room.peers["browser"].ws.send(json.dumps(candidate_msg))
                except Exception as e:
                    log.warning(f"Forward candidate to browser failed: {e}")
            else:
                room.pending_candidates.append(candidate_msg) if len(room.pending_candidates) < 128 else None
        else:
            # Forward to unity or queue early browser ICE until Unity arrives.
            if "unity" in room.peers:
                try:
                    await room.peers["unity"].ws.send(json.dumps(candidate_msg))
                except Exception as e:
                    log.warning(f"Forward candidate to unity failed: {e}")
            elif len(room.pending_candidates) < 128:
                room.pending_candidates.append(candidate_msg)
    
    elif msg_type == "ping":
        # Echo back with server timestamp
        try:
            await room.peers[sender_role].ws.send(json.dumps({
                "type": "pong",
                "t0": msg.get("t0"),
                "t1": msg.get("t1")
            }))
        except Exception:
            pass
    
    else:
        log.debug(f"[{room_id}] Unknown message type: {msg_type}")

async def main():
    parser = argparse.ArgumentParser(description="Phase 4 WebRTC Signaling Server")
    parser.add_argument("--host", default="127.0.0.1", help="Bind host")
    parser.add_argument("--port", type=int, default=8766, help="Bind port")
    args = parser.parse_args()
    
    log.info(f"Starting signaling server on {args.host}:{args.port}")
    log.info("Room: gallery (default), roles: unity, browser")
    log.info("Usage: ws://host:port/?room=gallery&role=unity")
    
    async with serve(handle_connection, args.host, args.port) as server:
        await server.serve_forever()

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        log.info("Server stopped")