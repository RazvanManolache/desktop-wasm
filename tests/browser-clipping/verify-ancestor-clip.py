import json, sys, time
from pathlib import Path
import argparse
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--label',default='clip')
parser.add_argument('--output',type=Path,default=root/'.artifacts/tmp/runs/ancestor-clip')
args=parser.parse_args()
out = args.output
out.mkdir(parents=True,exist_ok=True)
checks = []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    try:
        page = browser.new_page(viewport={'width':400,'height':240}, device_scale_factor=1)
        page.set_content('<canvas id="cliptest" width="200" height="150"></canvas>')
        page.add_script_tag(path=str(root/'src/WfWasm.CanvasHost/wwwroot/canvas-bridge.js'))
        result = page.evaluate('''() => {
          const c = document.getElementById('cliptest'), ctx = c.getContext('2d');
          const paint = ops => window.wfwasm.paint('cliptest', JSON.stringify(ops));
          const pixel = (x,y) => Array.from(ctx.getImageData(x,y,1,1).data);
          paint([[1,0,0,200,150,'#ffffffff'],[13,10,10,40,40],
                 [1,0,0,100,100,'#ff0000ff'],[13,30,30,40,40],
                 [1,0,0,100,100,'#0000ffff'],[14],
                 [1,12,12,4,4,'#00ff00ff'],[14],[1,100,10,5,5,'#000000ff']]);
          const nested = {outside:pixel(5,20),outer:pixel(20,20),inner:pixel(35,35),
                          intersection:pixel(60,35),restoredOuter:pixel(13,13),sibling:pixel(102,12)};
          paint([[1,0,0,200,150,'#ffffffff'],[13,20,20,0,40],
                 [1,0,0,200,150,'#ff0000ff'],[14]]);
          const empty = pixel(25,25);
          // An unmatched pop and unfinished push must not corrupt later frames.
          paint([[14],[13,0,0,1,1]]);
          paint([[1,80,80,3,3,'#000000ff']]);
          const frameRestore = pixel(81,81);
          // Clip must apply to uploaded layers and raw images, not only primitives.
          paint([[1,0,0,200,150,'#ffffffff']]);
          window.wfwasmTexDef('layer',new Uint8Array([255,0,0,255]),1,1);
          paint([[13,10,10,10,10],[9,'layer',0,0,60,60],
                 [7,0,30,60,30,1,1,btoa(String.fromCharCode(0,0,255,255))],[14]]);
          const layerInside=pixel(15,15),layerOutside=pixel(25,15),rawOutside=pixel(15,35);
          paint([[1,0,0,200,150,'#ffffffff']]);
          try { paint([[13,0,0,1,1],[2,0,0,20,20,-1,'#ff0000ff']]); }
          catch (e) { if (!(e instanceof RangeError)) throw e; }
          paint([[1,80,80,3,3,'#000000ff']]);
          const exceptionRestore = pixel(81,81);
          paint([[1,0,0,200,150,'#ffffffff'],[13,10,10,10,10],
                 [4,0,0,100,100,'#ff0000ff','#0000ffff'],
                 [5,0,25,150,25,4,'#000000ff'],
                 [6,0,0,100,100,3,'#000000ff','#00ff00ff'],
                 [3,'CLIP TEST',0,40,'30px sans-serif','#000000ff',0,0,-1,0,0,0,0],[14]]);
          const primitiveOutside = [pixel(50,10),pixel(100,25),pixel(30,35),pixel(20,50)];
          const fillText=ctx.fillText;
          ctx.fillText = () => { throw new Error('injected text paint failure'); };
          try { paint([[13,0,0,1,1],
                       [3,'x',0,0,'12px sans-serif','#000000ff',0,0,-1,0,0,1,1]]); }
          catch(e) { if(e.message !== 'injected text paint failure') throw e; }
          finally { ctx.fillText=fillText; }
          paint([[1,80,80,3,3,'#000000ff']]);
          const textExceptionRestore=pixel(81,81);
          return {textExceptionRestore,primitiveOutside,exceptionRestore,nested,empty,frameRestore,layerInside,layerOutside,rawOutside};
        }''')
        expected = {'nested':{'outside':[255,255,255,255],'outer':[255,0,0,255],
                    'inner':[0,0,255,255],'intersection':[255,255,255,255],
                    'restoredOuter':[0,255,0,255],'sibling':[0,0,0,255]},
                    'textExceptionRestore':[0,0,0,255],'primitiveOutside':[[255,255,255,255]]*4,'exceptionRestore':[0,0,0,255],
                    'empty':[255,255,255,255], 'frameRestore':[0,0,0,255],
                    'layerInside':[255,0,0,255],'layerOutside':[255,255,255,255],
                    'rawOutside':[255,255,255,255]}
        for k,v in expected.items():
            checks.append({'name':k,'passed':result[k]==v,'actual':result[k],'expected':v})
        page.screenshot(path=str(out/(args.label+'-pixels.png')))
        data={'checked_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'checks':checks,
              'passed':all(c['passed'] for c in checks)}
        (out/(args.label+'-pixels.json')).write_text(json.dumps(data,indent=2))
        print(json.dumps(data))
    finally:
        browser.close()
sys.exit(0 if all(c['passed'] for c in checks) else 1)
