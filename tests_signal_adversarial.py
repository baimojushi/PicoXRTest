import asyncio, json, sys, unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent/'PicoXRTest-master'))
import phase4_signaling as s
from websockets.asyncio.server import serve
from websockets.asyncio.client import connect

class TestSignaling(unittest.IsolatedAsyncioTestCase):
 async def asyncSetUp(self):
  s.rooms.clear()
  self.server=await serve(s.handle_connection,'127.0.0.1',0)
  self.port=self.server.sockets[0].getsockname()[1]
 async def asyncTearDown(self):
  self.server.close();await self.server.wait_closed();s.rooms.clear()
 async def peers(self,order=('unity','browser')):
  clients={}
  for r in order:
   clients[r]=await connect(f'ws://127.0.0.1:{self.port}/?room=test&role={r}')
  # the first peer gets notification
  await asyncio.wait_for(clients[order[0]].recv(),1)
  return clients
 async def test_browser_offer_unity_answer(self):
  c=await self.peers()
  try:
   await c['browser'].send(json.dumps({'type':'offer','sdp':'browser-SDP'}))
   self.assertEqual(json.loads(await asyncio.wait_for(c['unity'].recv(),1)),{'type':'offer','sdp':'browser-SDP'})
   await c['unity'].send(json.dumps({'type':'answer','sdp':'unity-SDP'}))
   self.assertEqual(json.loads(await asyncio.wait_for(c['browser'].recv(),1)),{'type':'answer','sdp':'unity-SDP'})
   await c['browser'].send(json.dumps({'type':'candidate','candidate':'candidate:browser','sdpMid':'0'}))
   self.assertEqual(json.loads(await asyncio.wait_for(c['unity'].recv(),1))['candidate'],'candidate:browser')
  finally:
   for ws in c.values(): await ws.close()
 async def test_cached_offer_and_early_candidate(self):
  browser=await connect(f'ws://127.0.0.1:{self.port}/?room=test&role=browser')
  await browser.send(json.dumps({'type':'offer','sdp':'early-offer'}))
  await browser.send(json.dumps({'type':'candidate','candidate':'early-ice'}))
  await asyncio.sleep(.04)
  unity=await connect(f'ws://127.0.0.1:{self.port}/?room=test&role=unity')
  try:
   values=[json.loads(await asyncio.wait_for(unity.recv(),1)) for _ in range(2)]
   self.assertEqual([v['type'] for v in values],['offer','candidate'])
  finally: await browser.close(); await unity.close()
 async def test_wrong_direction_rejected(self):
  c=await self.peers()
  try:
   await c['unity'].send(json.dumps({'type':'offer','sdp':'wrong'}))
   with self.assertRaises(asyncio.TimeoutError): await asyncio.wait_for(c['browser'].recv(),.15)
   await c['browser'].send(json.dumps({'type':'answer','sdp':'wrong'}))
   with self.assertRaises(asyncio.TimeoutError): await asyncio.wait_for(c['unity'].recv(),.15)
  finally:
   for ws in c.values(): await ws.close()
 async def test_missing_role_rejected(self):
  ws=await connect(f'ws://127.0.0.1:{self.port}/?room=test')
  from websockets.exceptions import ConnectionClosedError
  with self.assertRaises(ConnectionClosedError): await ws.recv()
 async def test_same_role_rejected(self):
  ws=await connect(f'ws://127.0.0.1:{self.port}/?room=test&role=unity')
  ws2=await connect(f'ws://127.0.0.1:{self.port}/?room=test&role=unity')
  from websockets.exceptions import ConnectionClosedError
  with self.assertRaises(ConnectionClosedError): await ws2.recv()
  await ws.close()

if __name__=='__main__':unittest.main(verbosity=2)
