import struct, sys, json
from pathlib import Path

TABLES={
0:'Module',1:'TypeRef',2:'TypeDef',3:'FieldPtr',4:'Field',5:'MethodPtr',6:'MethodDef',7:'ParamPtr',8:'Param',9:'InterfaceImpl',10:'MemberRef',11:'Constant',12:'CustomAttribute',13:'FieldMarshal',14:'DeclSecurity',15:'ClassLayout',16:'FieldLayout',17:'StandAloneSig',18:'EventMap',19:'EventPtr',20:'Event',21:'PropertyMap',22:'PropertyPtr',23:'Property',24:'MethodSemantics',25:'MethodImpl',26:'ModuleRef',27:'TypeSpec',28:'ImplMap',29:'FieldRVA',30:'ENCLog',31:'ENCMap',32:'Assembly',33:'AssemblyProcessor',34:'AssemblyOS',35:'AssemblyRef',36:'AssemblyRefProcessor',37:'AssemblyRefOS',38:'File',39:'ExportedType',40:'ManifestResource',41:'NestedClass',42:'GenericParam',43:'MethodSpec',44:'GenericParamConstraint'}

def u16(b,o): return struct.unpack_from('<H',b,o)[0]
def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def u64(b,o): return struct.unpack_from('<Q',b,o)[0]

def csize(maxrows, bits): return 2 if maxrows < (1 << (16-bits)) else 4

def read_idx(b,o,size): return (u16(b,o),o+2) if size==2 else (u32(b,o),o+4)

def compressed(data,o):
    x=data[o]
    if x & 0x80==0:return x,o+1
    if x & 0xC0==0x80:return ((x&0x3f)<<8)|data[o+1],o+2
    return ((x&0x1f)<<24)|(data[o+1]<<16)|(data[o+2]<<8)|data[o+3],o+4

class Meta:
 def __init__(self,path):
  self.path=path; self.b=Path(path).read_bytes(); b=self.b
  pe=u32(b,0x3c); assert b[pe:pe+4]==b'PE\0\0'
  coff=pe+4; nsec=u16(b,coff+2); optsz=u16(b,coff+16); opt=coff+20; magic=u16(b,opt)
  dd=opt+(96 if magic==0x10b else 112); cli_rva=u32(b,dd+14*8); self.sections=[]
  sh=opt+optsz
  for i in range(nsec):
   p=sh+i*40; name=b[p:p+8].split(b'\0',1)[0].decode(errors='replace'); vs=u32(b,p+8); va=u32(b,p+12); rs=u32(b,p+16); rp=u32(b,p+20); self.sections.append((name,va,max(vs,rs),rp))
  cli=self.rva(cli_rva); md_rva=u32(b,cli+8); md=self.rva(md_rva); assert b[md:md+4]==b'BSJB'; self.md=md
  vlen=u32(b,md+12); p=(md+16+vlen+3)&~3; flags=u16(b,p); nstreams=u16(b,p+2); p+=4; streams={}
  for _ in range(nstreams):
   off=u32(b,p); size=u32(b,p+4); p+=8; e=b.index(0,p); name=b[p:e].decode(); p=(e+4)&~3; streams[name]=(md+off,size)
  self.streams=streams; self.strings=b[streams['#Strings'][0]:sum(streams['#Strings'])]; self.blob=b[streams['#Blob'][0]:sum(streams['#Blob'])]
  st,size=streams.get('#~') or streams.get('#-'); self.tables_start=st; p=st; p+=4; major=b[p];minor=b[p+1]; heap=b[p+2];p+=4; valid=u64(b,p);p+=8; sortedmask=u64(b,p);p+=8
  self.heap=heap; self.strsz=4 if heap&1 else 2; self.guidsz=4 if heap&2 else 2; self.blobsz=4 if heap&4 else 2
  rows={i:0 for i in range(64)}
  for i in range(64):
   if valid>>i&1: rows[i]=u32(b,p);p+=4
  self.rows=rows; self.data_start=p
  self.offsets={}; q=p
  for i in range(45):
   if rows[i]:
    self.offsets[i]=q; q += rows[i]*self.rowsize(i)
  self.parse_types()
 def rva(self,rva):
  for name,va,sz,rp in self.sections:
   if va<=rva<va+sz:return rp+(rva-va)
  return rva
 def s(self,idx):
  if idx==0:return ''
  e=self.strings.find(b'\0',idx); return self.strings[idx:e].decode('utf-8','replace')
 def blobv(self,idx):
  if idx==0:return b''
  n,o=compressed(self.blob,idx); return self.blob[o:o+n]
 def tidx(self,t): return 2 if self.rows[t]<65536 else 4
 def coded(self,tables,bits): return csize(max(self.rows[t] for t in tables),bits)
 def rowsize(self,i):
  s=self.strsz; g=self.guidsz; bl=self.blobsz; tidx=self.tidx; coded=self.coded
  return {
   0:2+s+g*3,
   1:coded([0,26,35,1],2)+s+s, # ResolutionScope
   2:4+s+s+coded([2,1,27],2)+tidx(4)+tidx(6), # TypeDefOrRef
   3:tidx(4),
   4:2+s+bl,
   5:tidx(6),
   6:4+2+2+s+bl+tidx(8),
   7:tidx(8),
   8:2+2+s,
   9:tidx(2)+coded([2,1,27],2),
   10:coded([2,1,26,6,27],3)+s+bl,
   11:2+coded([4,8,23],2)+bl,
   12:coded([6,4,1,2,8,9,10,0,23,20,17,26,27,32,35,38,39,40,42,44,43],5)+coded([6,10],3)+bl, # likely custom attr type actually sparse tags; size okay max
   13:coded([4,8],1)+bl,
   14:2+coded([2,6,32],2)+bl,
   15:2+4+tidx(2),
   16:4+tidx(4),
   17:bl,
   18:tidx(2)+tidx(20),
   19:tidx(20),
   20:2+s+coded([2,1,27],2),
   21:tidx(2)+tidx(23),
   22:tidx(23),
   23:2+s+bl,
   24:2+tidx(6)+coded([20,23],1),
   25:tidx(2)+coded([6,10],1)+coded([6,10],1),
   26:s,
   27:bl,
   28:2+coded([4,6],1)+s+tidx(26),
   29:4+tidx(4),
   30:8,
   31:4,
   32:4+2+2+2+2+4+bl+s+s,
   33:4,
   34:4+4+4,
   35:2+2+2+2+4+bl+s+s+bl,
   36:4+tidx(35),
   37:4+4+4+tidx(35),
   38:4+s+bl,
   39:4+4+s+s+coded([38,35,39],2),
   40:4+4+s+coded([38,35,39],2),
   41:tidx(2)+tidx(2),
   42:2+2+coded([2,6],1)+s,
   43:coded([6,10],1)+bl,
   44:tidx(42)+coded([2,1,27],2),
  }[i]
 def row(self,t,rid): return self.offsets[t]+(rid-1)*self.rowsize(t)
 def parse_types(self):
  b=self.b;s=self.strsz;bl=self.blobsz
  typerefs={}
  for rid in range(1,self.rows[1]+1):
   p=self.row(1,rid); p+=self.coded([0,26,35,1],2); ni,p=read_idx(b,p,s); nsi,p=read_idx(b,p,s); typerefs[rid]=(self.s(nsi),self.s(ni))
  typedefs=[]
  for rid in range(1,self.rows[2]+1):
   p=self.row(2,rid); flags=u32(b,p);p+=4; ni,p=read_idx(b,p,s);nsi,p=read_idx(b,p,s); p+=self.coded([2,1,27],2); fi,p=read_idx(b,p,self.tidx(4));mi,p=read_idx(b,p,self.tidx(6)); typedefs.append({'rid':rid,'name':self.s(ni),'ns':self.s(nsi),'field_start':fi,'method_start':mi})
  for i,t in enumerate(typedefs):
   t['field_end']=(typedefs[i+1]['field_start'] if i+1<len(typedefs) else self.rows[4]+1)
   t['method_end']=(typedefs[i+1]['method_start'] if i+1<len(typedefs) else self.rows[6]+1)
  self.typedefs=typedefs; self.typerefs=typerefs
  # token maps
  self.type_names={0x02000000|t['rid']: self.full(t['ns'],t['name']) for t in typedefs}
  self.type_names.update({0x01000000|rid:self.full(ns,nm) for rid,(ns,nm) in typerefs.items()})
 def full(self,ns,n): return f'{ns}.{n}' if ns else n
 def typedef(self,name):
  for t in self.typedefs:
   if t['name']==name or self.full(t['ns'],t['name'])==name:return t
  return None
 def decode_typedeforref(self,val):
  tag=val&3; rid=val>>2; table={0:2,1:1,2:27}.get(tag)
  if not table:return f'?{val}'
  if table==27:return f'TypeSpec#{rid}'
  return self.type_names.get((table<<24)|rid,f'{TABLES[table]}#{rid}')
 def sig_type(self,data,o):
  # custom modifiers/pinned skipped
  et=data[o];o+=1
  prim={0x01:'System.Void',0x02:'System.Boolean',0x03:'System.Char',0x04:'System.SByte',0x05:'System.Byte',0x06:'System.Int16',0x07:'System.UInt16',0x08:'System.Int32',0x09:'System.UInt32',0x0a:'System.Int64',0x0b:'System.UInt64',0x0c:'System.Single',0x0d:'System.Double',0x0e:'System.String',0x18:'System.IntPtr',0x19:'System.UIntPtr',0x1c:'System.Object',0x16:'System.TypedReference'}
  if et in prim:return prim[et],o
  if et in (0x11,0x12):
   val,o=compressed(data,o); return self.decode_typedeforref(val),o
  if et==0x0f:
   x,o=self.sig_type(data,o);return x+'*',o
  if et==0x10:
   x,o=self.sig_type(data,o);return x+'&',o
  if et==0x1d:
   x,o=self.sig_type(data,o);return x+'[]',o
  if et==0x13:
   n,o=compressed(data,o);return f'!{n}',o
  if et==0x1e:
   n,o=compressed(data,o);return f'!!{n}',o
  if et==0x15:
   kind=data[o];o+=1; val,o=compressed(data,o); base=self.decode_typedeforref(val); argc,o=compressed(data,o);args=[]
   for _ in range(argc):x,o=self.sig_type(data,o);args.append(x)
   return base+'<'+','.join(args)+'>',o
  if et==0x14: # array
   x,o=self.sig_type(data,o);rank,o=compressed(data,o); nums,o=compressed(data,o)
   for _ in range(nums):_,o=compressed(data,o)
   nlb,o=compressed(data,o)
   for _ in range(nlb):_,o=compressed(data,o)
   return x+'['+','*(rank-1)+']',o
  if et in (0x1f,0x20): # cmod
   _,o=compressed(data,o); return self.sig_type(data,o)
  if et==0x45:return self.sig_type(data,o)
  return f'ET_{et:02x}',o
 def field(self,rid):
  p=self.row(4,rid);flags=u16(self.b,p);p+=2;ni,p=read_idx(self.b,p,self.strsz);si,p=read_idx(self.b,p,self.blobsz); sig=self.blobv(si); typ='?'
  try:
   o=1 if sig and sig[0]==0x06 else 0;typ,o=self.sig_type(sig,o)
  except Exception as e:typ='ERR:'+str(e)
  return {'rid':rid,'name':self.s(ni),'type':typ,'flags':flags}
 def method(self,rid):
  p=self.row(6,rid);rva=u32(self.b,p);p+=4;impl=u16(self.b,p);p+=2;flags=u16(self.b,p);p+=2;ni,p=read_idx(self.b,p,self.strsz);si,p=read_idx(self.b,p,self.blobsz);pi,p=read_idx(self.b,p,self.tidx(8)); sig=self.blobv(si);ret='?';params=[]
  try:
   o=0; call=sig[o];o+=1
   if call&0x10:gen,o=compressed(sig,o)
   n,o=compressed(sig,o);ret,o=self.sig_type(sig,o)
   for _ in range(n): x,o=self.sig_type(sig,o);params.append(x)
  except Exception as e:ret='ERR:'+str(e)
  return {'rid':rid,'name':self.s(ni),'return':ret,'params':params,'flags':flags,'rva':rva}
 def members(self,name):
  t=self.typedef(name)
  if not t:return None
  return {'type':self.full(t['ns'],t['name']),'fields':[self.field(i) for i in range(t['field_start'],t['field_end'])], 'methods':[self.method(i) for i in range(t['method_start'],t['method_end'])]}



def audit(meta, contract):
    checks=[]
    for type_name, spec in contract.get('types', {}).items():
        members=meta.members(type_name)
        if members is None:
            checks.append({'kind':'type','member':type_name,'passed':False,'detail':'missing type'})
            continue
        checks.append({'kind':'type','member':type_name,'passed':True,'detail':None})
        fields={f['name']:f for f in members['fields']}
        methods=members['methods']
        for field_name, expected_type in spec.get('fields', {}).items():
            actual=fields.get(field_name)
            passed=actual is not None and (not expected_type or actual['type']==expected_type)
            detail=None if passed else ('missing field' if actual is None else f"expected {expected_type}, found {actual['type']}")
            checks.append({'kind':'field','member':f'{type_name}.{field_name}','passed':passed,'detail':detail})
        for expected in spec.get('methods', []):
            name=expected['name']; params=expected.get('params',[]); ret=expected.get('return')
            candidates=[m for m in methods if m['name']==name]
            matched=[m for m in candidates if m['params']==params and (not ret or m['return']==ret)]
            passed=bool(matched)
            if passed: detail=None
            elif not candidates: detail='missing method'
            else: detail='found: '+', '.join(f"{m['return']} {name}({', '.join(m['params'])})" for m in candidates)
            checks.append({'kind':'method','member':f"{type_name}.{name}({', '.join(params)})",'passed':passed,'detail':detail})
    return checks


def main(argv):
    if len(argv)<3:
        print('Usage: python tools/audit_assembly.py <Assembly-CSharp.dll> <contract.json>', file=sys.stderr)
        return 2
    dll=Path(argv[1]); contract_path=Path(argv[2])
    if not dll.is_file() or not contract_path.is_file():
        print('DLL or contract path not found.', file=sys.stderr); return 2
    import hashlib
    meta=Meta(dll)
    contract=json.loads(contract_path.read_text(encoding='utf-8'))
    checks=audit(meta,contract)
    failed=[c for c in checks if not c['passed']]
    output={
      'assembly': {'path':str(dll),'sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),
                   'typeCount':meta.rows[2],'methodCount':meta.rows[6],'fieldCount':meta.rows[4]},
      'contract':str(contract_path),
      'summary':{'total':len(checks),'passed':len(checks)-len(failed),'failed':len(failed)},
      'checks':checks
    }
    print(json.dumps(output,indent=2))
    return 1 if failed else 0

if __name__=='__main__':
    raise SystemExit(main(sys.argv))
