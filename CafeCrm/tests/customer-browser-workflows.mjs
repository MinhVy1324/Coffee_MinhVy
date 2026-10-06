// Kiểm tra trình duyệt qua Edge/Chrome DevTools, không cần npm/Playwright.
// Dùng browser profile và database thử riêng. DevTools phải đang nghe cổng 9337.
// node tests/customer-browser-workflows.mjs http://127.0.0.1:5264 output-dir docs/customer-browser-verification.json
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
const base=process.argv[2]||'http://127.0.0.1:5264', output=process.argv[3]||'customer-browser-output';
const checks=[], suffix=Date.now().toString(36), email=`browser-${suffix}@cafe.test`, password='CafeDemo@123';
await mkdir(output,{recursive:true});
const target=(await (await fetch('http://127.0.0.1:9337/json')).json()).find(x=>x.type==='page');
assert.ok(target,'Open an isolated headless browser with remote-debugging-port=9337 first');
const socket=new WebSocket(target.webSocketDebuggerUrl), pending=new Map(); let sequence=0;
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
socket.onmessage=event=>{
    const message=JSON.parse(event.data), request=pending.get(message.id);
    if(!request)return; pending.delete(message.id);clearTimeout(request.timer);
    if(message.error)request.reject(new Error(JSON.stringify(message.error)));else request.resolve(message.result);
};
function send(method,params={}){return new Promise((resolve,reject)=>{const id=++sequence,timer=setTimeout(()=>reject(new Error(`Timeout ${method}`)),20000);pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression){const data=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(data.exceptionDetails)throw new Error(JSON.stringify(data.exceptionDetails));return data.result.value;}
async function wait(expression){const until=Date.now()+20000;while(Date.now()<until){if(await evaluate(expression))return;await new Promise(r=>setTimeout(r,100));}throw new Error(`Timeout ${expression}`);}
async function navigate(path){await send('Page.navigate',{url:base+path});await wait(`location.pathname+location.search===${JSON.stringify(path)} && document.readyState==='complete' && !!document.querySelector('h1')`);}
async function check(name,expression){assert.equal(await evaluate(expression),true,name);checks.push(name);console.log('PASS',name);}
async function viewport(width,height=960){await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<600});await evaluate('new Promise(resolve=>setTimeout(resolve,250))');}
async function screenshot(name){const data=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});await writeFile(join(output,name),Buffer.from(data.data,'base64'));}
async function api(method,path,body,token){const response=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{})},body:body===undefined?undefined:JSON.stringify(body)});const raw=await response.text();assert.ok(response.ok,`${response.status} ${raw.slice(0,300)}`);return raw?JSON.parse(raw):null;}
try {
    await send('Page.enable');await send('Runtime.enable');await send('Network.enable');await send('Network.clearBrowserCookies');
    await viewport(1440);await navigate('/Account/Register');
    await evaluate(`document.getElementById('register-name').value='Khách trình duyệt';document.getElementById('register-email').value=${JSON.stringify(email)};document.getElementById('register-password').value='CafeDemo@123';document.getElementById('register-confirm').value='Wrong@123';document.getElementById('register-confirm').dispatchEvent(new Event('input',{bubbles:true}));`);
    await check('Confirmation mismatch prevents native form submission',`!document.querySelector('form[action="/Account/Register"]').checkValidity() && document.getElementById('register-confirm').validationMessage.includes('Hai mật khẩu')`);
    await evaluate(`document.querySelector('[data-password-toggle="register-password"]').click()`);
    await check('Password toggle reveals the intended input accessibly',`document.getElementById('register-password').type==='text' && document.querySelector('[data-password-toggle="register-password"]').getAttribute('aria-pressed')==='true'`);
    await evaluate(`document.querySelector('[data-password-toggle="register-password"]').click();document.getElementById('register-password').value='abcdefgh';document.getElementById('register-confirm').value='abcdefgh';document.getElementById('register-confirm').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('form[action="/Account/Register"]').requestSubmit()`);
    await wait(`document.querySelector('.validation-summary-errors')?.textContent.includes('Mật khẩu cần có chữ hoa')`);
    await check('Weak password gives a server error without exposing entered passwords',`document.getElementById('register-email').value===${JSON.stringify(email)} && document.getElementById('register-password').value==='' && document.getElementById('register-confirm').value===''`);
    await viewport(393,852);await check('Mobile registration page has no horizontal overflow',`document.documentElement.scrollWidth<=innerWidth+1`);await screenshot('register-mobile.png');
    await evaluate(`document.getElementById('register-password').value='CafeDemo@123';document.getElementById('register-confirm').value='CafeDemo@123';document.getElementById('register-confirm').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('form[action="/Account/Register"]').requestSubmit()`);
    await wait(`location.pathname==='/Account/Login' && document.querySelector('.notice')?.textContent.includes('Đăng ký thành công')`);
    await check('Registration success notice is visible to an anonymous visitor',`!!document.querySelector('.notice[role="status"]')`);
    await evaluate(`document.getElementById('login-email').value=${JSON.stringify(email)};document.getElementById('login-password').value='CafeDemo@123';document.querySelector('form[action="/Account/Login"]').requestSubmit()`);
    await wait(`location.pathname==='/Portal' && document.querySelector('h1')?.textContent.includes('Khách trình duyệt')`);
    await check('Customer signs in to a personalized dashboard',`document.querySelectorAll('.portal-metrics .stat-card').length===3`);
    await check('Mobile sidebar opens and closes accessibly',`(()=>{document.getElementById('menu-toggle').click();const opened=document.getElementById('menu-toggle').getAttribute('aria-expanded')==='true';document.querySelector('[data-close-nav]').click();return opened && document.getElementById('menu-toggle').getAttribute('aria-expanded')==='false'})()`);
    for(const [path,name] of [['/Portal','dashboard'],['/Portal/Profile','profile'],['/Portal/Feedback','feedback'],['/Portal/Surveys','surveys'],['/Portal/ChangePassword','password']]) {
        await navigate(path);await viewport(1440);await check(`Desktop customer layout: ${name}`,`document.documentElement.scrollWidth<=innerWidth+1`);await screenshot(`${name}-desktop.png`);
        await viewport(393,852);await check(`Mobile customer layout: ${name}`,`document.documentElement.scrollWidth<=innerWidth+1`);await screenshot(`${name}-mobile.png`);
    }
    await navigate('/Portal/Profile');
    await evaluate(`document.getElementById('profile-name').value='Khách trình duyệt mới';document.getElementById('profile-birth').value='2000-01-01';document.querySelector('input[name="PreferenceIds"]').checked=true;document.querySelector('.profile-form').requestSubmit()`);
    await wait(`document.querySelector('.notice')?.textContent.includes('Đã lưu hồ sơ') && location.pathname==='/Portal/Profile'`);
    await check('Profile preferences survive a real form submission',`document.getElementById('profile-name').value==='Khách trình duyệt mới' && document.querySelector('input[name="PreferenceIds"]').checked`);
    await navigate('/Portal/ChangePassword');
    await evaluate(`document.getElementById('password-current').value='CafeDemo@123';document.getElementById('password-new').value='CafeNew@456';document.getElementById('password-confirm').value='Wrong@456';document.getElementById('password-confirm').dispatchEvent(new Event('input',{bubbles:true}))`);
    await check('Change password confirmation also prevents mismatched submission',`!document.querySelector('form[action="/Portal/ChangePassword"]').checkValidity()`);
    await navigate('/Portal/Feedback');
    await evaluate(`document.getElementById('feedback-product').selectedIndex=1;const input=document.getElementById('feedback-content');input.value='Cà phê thơm, phục vụ chu đáo.';input.dispatchEvent(new Event('input',{bubbles:true}))`);
    await check('Feedback character counter tracks input',`document.getElementById('feedback-counter').textContent.startsWith(document.getElementById('feedback-content').value.length.toString())`);
    await evaluate(`document.querySelector('form[action="/Portal/Feedback"][method="post"]').requestSubmit()`);
    await wait(`document.querySelector('.notice')?.textContent.includes('Đã gửi phản hồi') && document.querySelectorAll('.feedback-item').length===1`);
    await check('Feedback appears in the customer history',`document.querySelector('.feedback-item').textContent.includes('Cà phê thơm, phục vụ chu đáo.')`);

    const manager=(await api('POST','/api/auth/login',{email:'manager@cafe.test',password})).accessToken;
    const customer=(await api('POST','/api/auth/login',{email,password})).accessToken;
    const profile=await api('GET','/api/customer/profile',undefined,customer);
    const surveyId=(await api('POST','/api/crm/surveys',{title:'Khảo sát khách trình duyệt',description:'Giúp quán hiểu bạn hơn.',closesAtUtc:new Date(Date.now()+86400000).toISOString(),questions:[{text:'Bạn muốn cải thiện điều gì?',kind:1,isRequired:true,options:['Đồ uống','Không gian']},{text:'Bạn chấm mấy điểm?',kind:2,isRequired:true,options:[]},{text:'Góp ý thêm',kind:3,isRequired:false,options:[]}]},manager)).id;
    const survey=(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===surveyId);
    await api('POST',`/api/crm/surveys/${surveyId}/publish`,{customerIds:[profile.id],concurrencyToken:survey.concurrencyToken},manager);
    await navigate('/Portal');
    await check('Dashboard updates with the real received invitation',`document.querySelector('.portal-metrics strong').textContent==='1' && document.querySelector('.portal-dashboard').textContent.includes('Khảo sát khách trình duyệt')`);
    await viewport(1440);await screenshot('dashboard-with-invitation-desktop.png');await viewport(393,852);await screenshot('dashboard-with-invitation-mobile.png');
    await navigate('/Portal/Surveys?status=pending');
    await check('Pending survey tab is active and lists the invitation',`document.querySelector('.portal-tabs a.active').textContent.includes('Chờ trả lời') && document.querySelectorAll('.survey-item').length===1`);
    await navigate(`/Portal/Survey/${surveyId}`);
    await check('Required multiple choice prevents an empty response',`!document.getElementById('survey-response').checkValidity()`);
    await evaluate(`const input=document.querySelector('input[type="checkbox"]');input.checked=true;input.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('.response-question select').value='5'`);
    await check('Optional text question can be skipped',`document.getElementById('survey-response').checkValidity()`);
    await viewport(1440);await check('Desktop survey form has no horizontal overflow',`document.documentElement.scrollWidth<=innerWidth+1`);await screenshot('survey-form-desktop.png');
    await viewport(393,852);await check('Mobile survey form has no horizontal overflow',`document.documentElement.scrollWidth<=innerWidth+1`);await screenshot('survey-form-mobile.png');
    await evaluate(`document.getElementById('survey-response').requestSubmit()`);
    await wait(`location.pathname==='/Portal/Surveys' && document.querySelector('.notice')?.textContent.includes('Cảm ơn bạn đã trả lời')`);
    await navigate(`/Portal/Survey/${surveyId}`);
    await check('Completed survey renders saved answers and no submission form',`!document.getElementById('survey-response') && document.querySelectorAll('.saved-answer').length===3 && document.querySelector('.saved-answer').textContent.includes('Đồ uống')`);
    await screenshot('survey-receipt-mobile.png');
    await navigate('/Portal/Surveys?status=answered');await screenshot('survey-inbox-mobile.png');
    await check('Answered tab opens a saved response link',`document.querySelector('.survey-item a').textContent.includes('Xem câu trả lời đã gửi')`);
    await evaluate(`document.querySelector('form[action="/Account/Logout"]').requestSubmit()`);
    await wait(`location.pathname==='/Account/Login' && document.readyState==='complete'`);
    await check('Logout returns to the public login page',`!document.querySelector('.sidebar')`);
    const summary={verifiedAtUtc:new Date().toISOString(),base,checks:checks.length,passed:checks,customerEmail:email,screenshots:output};
    if(process.argv[4])await writeFile(process.argv[4],JSON.stringify(summary,null,2)+'\n');
    console.log(`Verified ${checks.length} customer browser checks.`);
} finally {socket.close();}
