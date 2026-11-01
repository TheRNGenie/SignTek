import json,base64
import numpy as np
def load(f):
    j=json.load(open('/tmp/cap1610/'+f)); b=np.frombuffer(base64.b64decode(j['data']),dtype=np.uint8)
    sig={q:((b>>i)&1).astype(int) for i,q in enumerate(j['header_pins'])}
    oe=sig[5]; fe=np.where((oe[:-1]==1)&(oe[1:]==0))[0]+1
    return sig,fe
N=246
# rows + P2 + P12/P14 from the newest set; start at a row-0 boundary
sA,feA=load('capture-20261101-160937.json')
k0=next(k for k in range(len(feA)-9) if sA[1][feA[k]+5]*4+sA[4][feA[k]+5]*2+sA[3][feA[k]+5]==0)
sB,feB=load('capture-20261101-012446.json')   # data 7, 8, 10, 11
sC,feC=load('capture-20261101-011448.json')   # timing 9 (=13)
words=[]
for r in range(8):
    a=feA[k0+r]; b=feB[r+2]; c=feC[r+2]
    for t in range(N):
        w=0
        if t==95: w|=1<<0                     # latch: short pulse just before the row change (from the 10 MHz captures)
        if sA[5][a+t]: w|=1<<1                # OE
        if sA[3][a+t]: w|=1<<2                # row A (pin 3)
        if sA[4][a+t]: w|=1<<3                # row B (pin 4)
        if sA[1][a+t]: w|=1<<4                # row C (pin 1)
        if sB[7][b+t]: w|=1<<5
        if sB[8][b+t]: w|=1<<6
        if sB[10][b+t]: w|=1<<7
        if sB[11][b+t]: w|=1<<8
        if sC[9][c+t]: w|=1<<9                # pins 9+13
        if sA[2][a+t]: w|=1<<10               # pin 2
        if sA[12][a+t]: w|=1<<11              # pin 12 (not wired on the Uno)
        if sA[14][a+t]: w|=1<<12              # pin 14 (not wired on the Uno)
        words.append(w)
print(len(words), 'row0 at', k0)
# sanity: rows per 246 block
for r in range(8):
    blk=words[r*N:(r+1)*N]; w=blk[150]; print(r, 'row', (w>>2&1)|(w>>3&1)<<1|(w>>4&1)<<2, 'oe hi', sum(x>>1&1 for x in blk), 'p2 lo', sum(1-(x>>10&1) for x in blk), 't1 lo', sum(1-(x>>9&1) for x in blk), 'data7', sum(x>>5&1 for x in blk))
open('/home/user/signtek/app/uno-replay.txt','w').write(' '.join('%04x'%w for w in words)+'\n')
