"""Integration checks against a fresh Development demo. Python 3 standard library only.
Run: python tests/smoke.py http://localhost:5240
Uses synthetic accounts; no external communication or services.
"""
import json, sys, uuid, urllib.request, urllib.error, datetime, http.cookiejar, re
BASE = sys.argv[1] if len(sys.argv)>1 else 'http://localhost:5240'
checks=[]
def call(method,path,body=None,token=None,expected=200,label=None):
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    req=urllib.request.Request(BASE+path,data=json.dumps(body).encode() if body is not None else None,headers=headers,method=method)
    try:
        with urllib.request.urlopen(req,timeout=20) as response: status=response.status; raw=response.read()
    except urllib.error.HTTPError as error: status=error.code; raw=error.read()
    if status!=expected: raise AssertionError(f'{method} {path}: expected {expected}, got {status}: {raw[:600]!r}')
    checks.append(label or f'{method} {path} -> {status}')
    return json.loads(raw) if raw and raw.startswith((b'{',b'[')) else raw.decode()
def login(email): return call('POST','/api/auth/login',{'email':email,'password':'CafeDemo@123'})
admin=login('admin@cafe.test')['accessToken']; manager=login('manager@cafe.test')['accessToken']; staff=login('staff@cafe.test')['accessToken']
customer_tokens=login('customer@cafe.test'); customer=customer_tokens['accessToken']
call('GET','/api/customer/profile',expected=401,label='Unauthenticated request denied')
call('GET','/api/admin/users',token=manager,expected=403,label='Manager cannot access Admin')
call('GET','/api/crm/reports',token=staff,expected=403,label='Staff cannot access reports')
call('GET','/api/crm/customers',token=customer,expected=403,label='Customer cannot access customer list')
call('GET','/api/customer/profile',token=admin,expected=403,label='Admin has separate workspace')
email='smoke-'+uuid.uuid4().hex[:8]+'@cafe.test'
call('POST','/api/auth/register',{'email':email,'password':'CafeDemo@123','fullName':'Khách thử nghiệm'},expected=201)
call('POST','/api/auth/register',{'email':email,'password':'CafeDemo@123','fullName':'Khách thử nghiệm'},expected=400,label='Duplicate email denied')
new_customer=login(email)['accessToken']
profile=call('GET','/api/customer/profile',token=new_customer)
preferences=call('GET','/api/customer/preferences',token=new_customer)
update={'fullName':'Khách thử nghiệm','phone':None,'birthDate':'2003-10-02','address':'Quận 1','preferenceIds':[preferences[0]['id']],'concurrencyToken':profile['concurrencyToken']}
call('PUT','/api/customer/profile',update,new_customer,expected=204)
call('PUT','/api/customer/profile',update,new_customer,expected=409,label='Stale profile version denied')
profile=call('GET','/api/customer/profile',token=new_customer)
update['concurrencyToken']=profile['concurrencyToken']; update['birthDate']='2999-01-01'
call('PUT','/api/customer/profile',update,new_customer,expected=400,label='Future birthdate denied')
update['birthDate']='2003-10-02';update['preferenceIds']=[99999]
call('PUT','/api/customer/profile',update,new_customer,expected=400,label='Unknown preference denied')
products=call('GET','/api/customer/products',token=new_customer)
fid=call('POST','/api/customer/feedback',{'productId':products[0]['id'],'rating':5,'content':'<script>alert(1)</script> Cà phê ngon'},new_customer)['id']
call('POST','/api/customer/feedback',{'productId':None,'rating':7,'content':'Sai điểm'},new_customer,expected=400,label='Rating out of range denied')
feedback=next(x for x in call('GET','/api/crm/feedback',token=staff) if x['id']==fid)
call('POST',f'/api/crm/feedback/{fid}/replies',{'content':'Cảm ơn bạn','status':2,'concurrencyToken':feedback['concurrencyToken']},staff,expected=204)
assert call('GET','/api/customer/feedback',token=new_customer)[0]['replies'][0]['content']=='Cảm ơn bạn'
checks.append('Customer sees staff reply')
deadline=(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(days=7)).isoformat()
draft={'title':'Thăm dò món mới','description':'Khảo sát demo','closesAtUtc':deadline,'questions':[
 {'text':'Bạn chọn món nào?','kind':0,'isRequired':True,'options':['Cold brew','Latte']},
 {'text':'Mức hài lòng?','kind':2,'isRequired':True,'options':[]},
 {'text':'Sở thích thêm?','kind':1,'isRequired':False,'options':['Ít đường','Nhiều đá']},
 {'text':'Góp ý','kind':3,'isRequired':False,'options':[]}]}
invalid=dict(draft,questions=[])
call('POST','/api/crm/surveys',invalid,manager,expected=400,label='Empty survey denied')
sid=call('POST','/api/crm/surveys',draft,manager)['id']
survey=next(s for s in call('GET','/api/crm/surveys',token=manager) if s['id']==sid)
call('GET',f'/api/customer/surveys/{sid}',token=new_customer,expected=404,label='Uninvited customer denied')
call('POST',f'/api/crm/surveys/{sid}/publish',{'customerIds':[profile['id']],'concurrencyToken':survey['concurrencyToken']},manager)
q=call('GET',f'/api/customer/surveys/{sid}',token=new_customer)['questions']
call('GET',f'/api/customer/surveys/{sid}',token=customer,expected=404,label='Another customer cannot read targeted survey')
call('POST',f'/api/customer/surveys/{sid}/responses',{'answers':[]},new_customer,expected=400,label='Missing required answers denied')
invalid_answers={'answers':[{'questionId':q[0]['id'],'optionIds':[q[2]['options'][0]['id']],'rating':None,'text':None}]}
call('POST',f'/api/customer/surveys/{sid}/responses',invalid_answers,new_customer,expected=400,label='Option from another question denied')
answers={'answers':[
 {'questionId':q[0]['id'],'optionIds':[q[0]['options'][0]['id']],'rating':None,'text':None},
 {'questionId':q[1]['id'],'optionIds':[],'rating':5,'text':None},
 {'questionId':q[2]['id'],'optionIds':[o['id'] for o in q[2]['options']],'rating':None,'text':None},
 {'questionId':q[3]['id'],'optionIds':[],'rating':None,'text':'Rất hài lòng'}]}
repeat={'answers':answers['answers']+[answers['answers'][0]]}
call('POST',f'/api/customer/surveys/{sid}/responses',repeat,new_customer,expected=400,label='Repeated question denied')
call('POST',f'/api/customer/surveys/{sid}/responses',answers,new_customer)
call('POST',f'/api/customer/surveys/{sid}/responses',answers,new_customer,expected=409,label='Duplicate response denied')
result=call('GET',f'/api/crm/surveys/{sid}/results',token=manager)
assert result['responded']==1 and result['invited']==1 and result['responseRate']==100
assert result['questions'][1]['averageRating']==5 and sum(o['percentage'] for o in result['questions'][2]['options'])==200
checks.append('Survey statistics and multi-select denominator correct')
survey=next(s for s in call('GET','/api/crm/surveys',token=manager) if s['id']==sid)
call('PUT',f'/api/crm/surveys/{sid}',{'draft':draft,'concurrencyToken':survey['concurrencyToken']},manager,expected=409,label='Published questions immutable')
publish=call('POST',f'/api/crm/surveys/{sid}/publish',{'customerIds':[profile['id']],'concurrencyToken':survey['concurrencyToken']},manager)
assert publish['sent']==0; checks.append('Publishing again does not duplicate invitations')
survey=next(s for s in call('GET','/api/crm/surveys',token=manager) if s['id']==sid)
call('POST',f'/api/crm/surveys/{sid}/close',survey['concurrencyToken'],manager,expected=204)
call('POST',f'/api/customer/surveys/{sid}/responses',answers,new_customer,expected=409,label='Closed survey cannot accept answers')
call('POST',f"/api/crm/customers/{profile['id']}/lock",True,manager,expected=204)
call('GET','/api/customer/profile',token=new_customer,expected=401,label='Existing access token revoked immediately on lock')
call('POST',f"/api/crm/customers/{profile['id']}/lock",False,manager,expected=204)
call('GET','/api/customer/profile',token=new_customer,expected=401,label='Unlock does not restore old session')
new_customer=login(email)['accessToken']; profile=call('GET','/api/customer/profile',token=new_customer)
call('DELETE',f"/api/crm/customers/{profile['id']}?version={profile['concurrencyToken']}",token=manager,expected=204)
call('GET','/api/customer/profile',token=new_customer,expected=401,label='Archived customer is blocked')
assert any(f['id']==fid for f in call('GET','/api/crm/feedback',token=manager)); checks.append('Archiving preserves feedback history')
users=call('GET','/api/admin/users',token=admin); aid=next(x['id'] for x in users if x['email']=='admin@cafe.test')
call('POST',f'/api/admin/users/{aid}/lock',True,admin,expected=400,label='Admin cannot disable own account')
call('GET','/api/admin/products?minPrice=40000&sort=price_desc',token=admin)
report=call('GET','/api/crm/reports',token=manager)
assert sum(g['count'] for g in report['ageGroups'])==report['activeCustomers']; checks.append('Age groups include unknown birth dates')
# Web cookie login plus CSRF and encoded feedback rendering.
jar=http.cookiejar.CookieJar(); web=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
login_html=web.open(BASE+'/Account/Login').read().decode()
antiforgery=re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',login_html).group(1)
from urllib.parse import urlencode
request=urllib.request.Request(BASE+'/Account/Login',data=urlencode({'email':'manager@cafe.test','password':'CafeDemo@123','__RequestVerificationToken':antiforgery}).encode())
assert web.open(request).status==200
for path in ['/Crm','/Crm/Customers','/Crm/Feedback','/Crm/Surveys','/Crm/CreateSurvey','/Crm/Reports',f'/Crm/Results/{sid}']:
    with web.open(BASE+path) as r:
        html=r.read().decode(); assert r.status==200
        if path=='/Crm/Feedback': assert '&lt;script&gt;' in html and '<script>alert(1)</script>' not in html
    checks.append('Web renders '+path)
try: web.open(urllib.request.Request(BASE+'/Crm/Publish',data=urlencode({'id':sid}).encode())); raise AssertionError('Missing CSRF accepted')
except urllib.error.HTTPError as e: assert e.code==400
checks.append('Web form without CSRF token denied')
call('POST','/api/auth/refresh',{'refreshToken':customer_tokens['refreshToken']})
def web_login(email):
    session=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    html=session.open(BASE+'/Account/Login').read().decode()
    token=re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',html).group(1)
    session.open(urllib.request.Request(BASE+'/Account/Login',data=urlencode({'email':email,'password':'CafeDemo@123','__RequestVerificationToken':token}).encode())).read()
    return session
admin_web=web_login('admin@cafe.test')
for path in ['/Admin','/Admin/Products','/Admin/ProductEditor','/Admin/Suppliers','/Admin/SupplierEditor']:
    with admin_web.open(BASE+path) as r: assert r.status==200; r.read()
    checks.append('Admin web renders '+path)
customer_web=web_login('customer@cafe.test')
for path in ['/Portal','/Portal/Feedback','/Portal/Surveys']:
    with customer_web.open(BASE+path) as r: assert r.status==200; r.read()
    checks.append('Customer web renders '+path)
staff_id=next(x['id'] for x in users if x['email']=='staff@cafe.test')
call('PUT',f'/api/admin/users/{staff_id}/role',{'role':'Manager'},admin,expected=204)
call('GET','/api/crm/customers',token=staff,expected=401,label='Changing role invalidates previously issued token')
call('PUT',f'/api/admin/users/{staff_id}/role',{'role':'Staff'},admin,expected=204)
out={'passed':len(checks),'checks':checks,'scope':'Web and API on SQLite demo; SQL Server execution and Android device build not covered.'}
print(json.dumps(out,ensure_ascii=False,indent=2))
