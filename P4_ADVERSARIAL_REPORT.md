# Phase 4 adversarial patch report

## Source identity and limitations
- Input: PicoXRTest-master.zip (root archive marker 901a818b23f712ba4bd8959048015ffded64a821).
- Current ZIP includes GalleryRuntime.SplatZoneManager, NOT Gallery3DGS.Streaming.SplatZoneManager. Legacy Phase4WebRTCTest references absent API and cannot reproduce the claimed 8/8 with this snapshot.
- Unity executable and WebRTC native runtime are not installed in this execution environment. No Unity compilation, actual PlayMode, real H.264 frame decode, or real Player transform movement is claimed.

## Changes
- Explicit room roles in both endpoints; browser sends Offer, Unity sends Answer; server relays with matching role enforcement. Early browser ICE buffered (max 128 messages). Removal of unordered implicit role assignment.
- DataChannel UDP serializes RemoteInputPacket fields seq,moveX,moveY,lookX,lookY,interact,reset with invariant decimal formatting.
- VideoPusher no longer modifies camera movement or look rotation; only forwards control bytes to bridge. On peer disconnect clears bridge state.
- On close, bridge clears inbox and output immediately. Only valid control messages refresh watchdog; ping cannot prolong stale movement; timeout defaults to 0.5 sec.
- Receiver bound to loopback; GalleryPlayerController resolves missing receiver reference dynamically as a fallback.
- Browser sends 20Hz continuously, stops/zeros input on blur, hidden tab and disconnect, removes duplicate intervals.
- Legacy Phase4WebRTCTest gated behind P4_LEGACY_STREAMING_TESTS and its prior false-positive control test replaced by explicit failure. New Phase4BridgeAdversarialTests uses actual loopback UDP packets and negative cases; REQUIRES UNITY TO EXECUTE.

## Actual local tests
- python -m py_compile phase4_signaling.py: PASS
- Python websockets real sockets (test_signaling.py): 5/5 PASS
  * browser Offer -> Unity and Unity Answer -> browser
  * browser-before-Unity cached Offer and ICE
  * invalid direction rejected (negative)
  * unspecified role rejected (negative)
  * duplicate role rejected (negative)
- First run: 4/5, exposing uncached early browser ICE. Patched, rerun: 5/5 PASS.

## Incomplete gates / blockers
- UNITY_COMPILATION: NOT RUN
- UNITY_PLAYMODE_BRIDGE_NEGATIVE: WRITTEN, NOT RUN
- REAL_BROWSER_WEBRTC_CONNECTED: NOT RUN
- H264_VIDEO_FRAMES_DECODED: NOT RUN
- REAL_3DGS_LOW_HIGH_VISIBLE: NOT RUN
- PLAYER_REMOTE_CONTROL_AND_ZONES: NOT RUN
- 30_MIN_RUNTIME/100_SWITCHES: NOT RUN
- INTERNET_HTTPS_WSS_TURN_SECURITY: NOT CONFIGURED (do not expose public 8766 as-is)
- Camera/viewTransform/receiver wiring in actual shipping scene requires inspection. Automatic receiver lookup is a fallback, not a guarantee of correct multi-player binding.

## Repro / execution next
1. Extract patched project; ensure Unity Package Manager resolves com.unity.webrtc 3.x and project's actual Gsplat version. Do not enable P4_LEGACY_STREAMING_TESTS unless its external Streaming implementation is installed.
2. Run Phase4BridgeAdversarialTests PlayMode in Unity; fix all compile errors before claiming success.
3. Open the actual shipping scene, assign one Player, CharacterController, remoteInput, viewTransform, SplatZoneManager.observer; confirm Camera.main renders splats.
4. Run signaling service in loopback, use explicit role URLs. Launch Unity with -webrtc; load test_webrtc.html from same host. Capture browser inbound-rtp framesDecoded and actual Player transform movement/zone transitions.
5. Block P4.5/P4.6 until real browser E2E evidence and disconnect negative tests pass.

Final status: PARTIAL_FIX_VERIFIED; P4_REAL_E2E_ADVERSARIAL_PASS = NOT ACHIEVED.
