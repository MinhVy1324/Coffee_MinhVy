// Kiểm thử nghiệp vụ khách bằng HTTP thật, không cần npm. Chỉ chạy trên database thử riêng.
// node tests/customer-workflows.mjs http://127.0.0.1:5263 docs/customer-verification.json
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
const base = process.argv[2] || 'http://127.0.0.1:5263';
const checks = [], suffix = Date.now().toString(36), password = 'CafeDemo@123', newPassword = 'CafeNew@456';
const email = `portal-${suffix}@cafe.test`, otherEmail = `other-${suffix}@cafe.test`;
const htmlText = html => html.replace(/&#x([0-9a-f]+);/gi,(_,code)=>String.fromCodePoint(parseInt(code,16)))
    .replace(/&#([0-9]+);/g,(_,code)=>String.fromCodePoint(Number(code))).replace(/&quot;/g,'"').replace(/&amp;/g,'&');
function passed(name, condition = true) { assert.ok(condition,name); checks.push(name); console.log('PASS',name); }
async function api(method,path,body,token,expected=200) {
    const response = await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token ? {Authorization:`Bearer ${token}`} : {})},body:body===undefined ? undefined : JSON.stringify(body)});
    const raw = await response.text();
    assert.equal(response.status,expected,`${method} ${path}: ${raw.slice(0,350)}`);
    return raw ? JSON.parse(raw) : null;
}
const loginApi = async (account,secret=password) => (await api('POST','/api/auth/login',{email:account,password:secret})).accessToken;
class Session {
    cookies = new Map();
    async request(path,options={}) {
        for (let redirect=0;redirect<8;redirect++) {
            const response = await fetch(base+path,{...options,redirect:'manual',headers:{...options.headers,Cookie:[...this.cookies].map(([k,v])=>`${k}=${v}`).join('; ')}});
            for (const cookie of response.headers.getSetCookie()) {
                const [pair] = cookie.split(';'), split=pair.indexOf('='), key=pair.slice(0,split), value=pair.slice(split+1);
                if (value) this.cookies.set(key,value); else this.cookies.delete(key);
            }
            if ([301,302,303].includes(response.status)) {
                const destination=new URL(response.headers.get('location'),base);
                assert.equal(destination.origin,new URL(base).origin,'Redirect stays inside the application');
                path=destination.pathname+destination.search; options={}; continue;
            }
            return {status:response.status,path,html:await response.text(),headers:response.headers};
        }
        throw new Error('Too many redirects');
    }
    async post(path,fields,source) {
        const page=await this.request(source), csrf=page.html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
        assert.ok(csrf,`CSRF form exists on ${source}`);
        const body=new URLSearchParams(Array.isArray(fields)?fields:Object.entries(fields)); body.set('__RequestVerificationToken',csrf);
        return this.request(path,{method:'POST',body});
    }
    login(account,secret=password,returnUrl='') { return this.post('/Account/Login',{email:account,password:secret,ReturnUrl:returnUrl},'/Account/Login'); }
}
const web=new Session(), guest=new Session();
let page=await guest.request('/Portal/Profile');
passed('Guest must sign in before accessing own profile',page.path.startsWith('/Account/Login?ReturnUrl='));
passed('Login preserves the requested local destination',page.html.includes('value="/Portal/Profile"'));
page=await guest.request('/Account/Register',{method:'POST',body:new URLSearchParams({email,password,fullName:'Khách thử',ConfirmPassword:password})});
passed('Registration rejects requests without CSRF',page.status===400);
page=await web.post('/Account/Register',{email,password,fullName:'Khách thử',ConfirmPassword:'Wrong@123'},'/Account/Register');
passed('Registration checks password confirmation on the server',htmlText(page.html).includes('Hai mật khẩu chưa trùng nhau'));
passed('Invalid registration never echoes passwords',!page.html.includes(password) && !page.html.includes('Wrong@123'));
page=await web.post('/Account/Register',{email,password:'abcdefgh',ConfirmPassword:'abcdefgh',fullName:'Khách thử'},'/Account/Register');
passed('Password policy returns Vietnamese messages',htmlText(page.html).includes('Mật khẩu cần có chữ hoa') && htmlText(page.html).includes('Mật khẩu cần có chữ số'));
page=await web.post('/Account/Register',{email,password,ConfirmPassword:password,fullName:'   '},'/Account/Register');
passed('Whitespace names are rejected',htmlText(page.html).includes('Vui lòng nhập họ tên'));
page=await web.post('/Account/Register',{email:`  ${email}  `,password,ConfirmPassword:password,fullName:'  Khách mới  ',Role:'Manager'},'/Account/Register');
passed('Registration creates account and profile with a clear success notice',page.path==='/Account/Login' && htmlText(page.html).includes('Đăng ký thành công'));
const authTokens=await api('POST','/api/auth/login',{email,password});
const token=authTokens.accessToken;
let profile=await api('GET','/api/customer/profile',undefined,token);
passed('Name/email are trimmed before saving',profile.fullName==='Khách mới' && profile.email===email);
await api('GET','/api/crm/customers',undefined,token,403); passed('Role injection cannot grant staff or manager access');
page=await web.post('/Account/Register',{email:email.toUpperCase(),password,ConfirmPassword:password,fullName:'Tên giữ lại'},'/Account/Register');
passed('Duplicate email is case insensitive and preserves nonsecret fields',htmlText(page.html).includes('Email này đã được đăng ký') && htmlText(page.html).includes('Tên giữ lại'));
page=await web.login('missing@cafe.test'); const unknownError=page.html.match(/<li>(.*?)<\/li>/s)?.[1];
page=await web.login(email,'Wrong@123');
passed('Wrong password and unknown email use the same failure message',page.html.match(/<li>(.*?)<\/li>/s)?.[1]===unknownError && !page.html.includes('Wrong@123'));
page=await web.login(email,password,'/Portal/Surveys'); passed('Successful login restores local ReturnUrl',page.path==='/Portal/Surveys');
await web.post('/Account/Logout',{},'/Portal');
page=await web.login(email,password,'https://example.com/phishing'); passed('External ReturnUrl is ignored',page.path==='/Portal');
passed('Customer dashboard shows own name and real survey metrics',htmlText(page.html).includes('Chào Khách mới!') && htmlText(page.html).includes('Khảo sát chờ bạn'));
page=await web.request('/Portal/Profile'); passed('Private profile responses disable caching',page.headers.get('cache-control')?.includes('no-store'));
await api('POST','/api/auth/register',{email:otherEmail,password,fullName:'Khách khác'},undefined,201);
const otherToken=await loginApi(otherEmail), otherProfile=await api('GET','/api/customer/profile',undefined,otherToken);
const preferences=await api('GET','/api/customer/preferences',undefined,token);
const profileFields={FullName:'Tên đã sửa',Phone:'',BirthDate:'2001-02-03',Address:'Địa chỉ mới',PreferenceIds:String(preferences[0].id),ConcurrencyToken:profile.concurrencyToken,Id:otherProfile.id,Email:otherEmail};
page=await web.post('/Portal/Update',profileFields,'/Portal/Profile');
passed('Profile saves and redirects to a fresh profile page',page.path==='/Portal/Profile' && htmlText(page.html).includes('Đã lưu hồ sơ'));
profile=await api('GET','/api/customer/profile',undefined,token);
passed('Forged customer ID/email cannot modify another account',profile.id!==otherProfile.id && profile.email===email && profile.fullName==='Tên đã sửa' && (await api('GET','/api/customer/profile',undefined,otherToken)).fullName==='Khách khác');
passed('Profile saves birthday/address and real preference IDs',profile.birthDate==='2001-02-03' && profile.address==='Địa chỉ mới' && profile.preferenceIds.includes(preferences[0].id));
page=await web.post('/Portal/Update',{FullName:profile.fullName,Phone:'',BirthDate:profile.birthDate,Address:profile.address,ConcurrencyToken:profile.concurrencyToken},'/Portal/Profile');
profile=await api('GET','/api/customer/profile',undefined,token);
passed('Profile can be saved without any preference selected',page.path==='/Portal/Profile' && profile.preferenceIds.length===0);
page=await web.post('/Portal/Update',{...profileFields,FullName:'Giữ dữ liệu khi lỗi',BirthDate:'2999-01-01',ConcurrencyToken:profile.concurrencyToken},'/Portal/Profile');
passed('Future birthdays are rejected and entered values are preserved',htmlText(page.html).includes('Ngày sinh không hợp lệ') && htmlText(page.html).includes('Giữ dữ liệu khi lỗi'));
page=await web.post('/Portal/Update',{...profileFields,BirthDate:'invalid-date',ConcurrencyToken:profile.concurrencyToken},'/Portal/Profile');
passed('Invalid date format returns a Vietnamese binding error',htmlText(page.html).includes('Thông tin không đúng định dạng'));
page=await web.post('/Portal/Update',profileFields,'/Portal/Profile'); passed('Stale profile versions cannot overwrite recent changes',htmlText(page.html).includes('Dữ liệu đã được sửa'));
await api('PUT','/api/customer/profile',{fullName:profile.fullName,phone:'123',birthDate:null,address:null,preferenceIds:[],concurrencyToken:profile.concurrencyToken},token,400); passed('Invalid phone is rejected by API');
await api('PUT','/api/customer/profile',{fullName:profile.fullName,phone:null,birthDate:null,address:null,preferenceIds:[2147483647],concurrencyToken:profile.concurrencyToken},token,400); passed('Unknown preferences are rejected');

const products=await api('GET','/api/customer/products',undefined,token);
assert.ok(products.length>0,'Demo contains an active product');
page=await web.post('/Portal/Feedback',{productId:products[0].id,rating:'0',content:'Nội dung giữ lại'},'/Portal/Feedback');
passed('Invalid feedback rating returns a form error with entered text',htmlText(page.html).includes('Điểm đánh giá phải từ 1 đến 5') && htmlText(page.html).includes('Nội dung giữ lại'));
page=await web.post('/Portal/Feedback',{productId:products[0].id,rating:'5',content:'a'.repeat(2001)},'/Portal/Feedback');
passed('Web service rejects oversized feedback even when HTML limits are bypassed',htmlText(page.html).includes('Nội dung phản hồi không quá 2.000 ký tự'));
await api('POST','/api/customer/feedback',{productId:2147483647,rating:5,content:'Thử sản phẩm lạ'},token,400); passed('Unknown products cannot receive feedback');
await api('POST','/api/customer/feedback',{productId:null,rating:5,content:' '.repeat(3)},token,400); passed('Blank feedback is rejected');
await api('POST','/api/customer/feedback',{productId:null,rating:5,content:'a'.repeat(2001)},token,400); passed('Oversized feedback is rejected');
const unsafe='<script>alert("portal")</script> Cà phê ngon';
page=await web.post('/Portal/Feedback',{productId:products[0].id,rating:'5',content:unsafe},'/Portal/Feedback');
passed('Product feedback is saved and HTML is encoded',page.path==='/Portal/Feedback' && page.html.includes('&lt;script&gt;') && !page.html.includes('<script>alert('));
const ownFeedback=await api('GET','/api/customer/feedback',undefined,token);
passed('Own feedback retains chosen product and rating',ownFeedback.length===1 && ownFeedback[0].productName===products[0].name && ownFeedback[0].rating===5);
passed('Other customer cannot see private feedback',(await api('GET','/api/customer/feedback',undefined,otherToken)).length===0);
page=await web.request('/Portal/Feedback'); passed('Reload after successful POST does not resend feedback',(await api('GET','/api/customer/feedback',undefined,token)).length===1);
const manager=await loginApi('manager@cafe.test'), staff=await loginApi('staff@cafe.test');
await api('POST',`/api/crm/feedback/${ownFeedback[0].id}/replies`,{content:'Quán đã tiếp nhận góp ý của bạn.',status:2,concurrencyToken:ownFeedback[0].concurrencyToken},staff,204);
page=await web.request('/Portal/Feedback?status=Resolved'); passed('Customer reads staff reply and filters by status',htmlText(page.html).includes('Quán đã tiếp nhận góp ý của bạn.') && htmlText(page.html).includes('Đã giải quyết'));
page=await web.request('/Portal/Feedback?status=New'); passed('Feedback filter hides other statuses',!page.html.includes('&lt;script&gt;'));

const draft={title:'Khảo sát riêng của khách',description:'Bốn loại câu hỏi và một câu không bắt buộc',closesAtUtc:new Date(Date.now()+86400000).toISOString(),questions:[
    {text:'Chọn một đồ uống',kind:0,isRequired:true,options:['Cà phê','Trà']},
    {text:'Chọn sở thích',kind:1,isRequired:true,options:['Ít đường','Nhiều đá']},
    {text:'Chấm điểm',kind:2,isRequired:true,options:[]},
    {text:'Góp ý thêm',kind:3,isRequired:true,options:[]},
    {text:'Câu có thể bỏ qua',kind:3,isRequired:false,options:[]}]};
const surveyId=(await api('POST','/api/crm/surveys',draft,manager)).id;
let survey=(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===surveyId);
await api('POST',`/api/crm/surveys/${surveyId}/publish`,{customerIds:[profile.id],concurrencyToken:survey.concurrencyToken},manager);
const inbox=await api('GET','/api/customer/surveys',undefined,token);
passed('Survey appears in the invited customer account',inbox.some(x=>x.surveyId===surveyId && !x.hasResponded));
await api('GET',`/api/customer/surveys/${surveyId}/response`,undefined,token,204); passed('Unanswered invitation has no saved response yet');
passed('Uninvited customer has no invitation',(await api('GET','/api/customer/surveys',undefined,otherToken)).length===0);
await api('GET',`/api/customer/surveys/${surveyId}`,undefined,otherToken,404);
await api('GET',`/api/customer/surveys/${surveyId}/response`,undefined,otherToken,404); passed('Uninvited customer cannot read survey or saved answers');
const otherWeb=new Session(); await otherWeb.login(otherEmail);
page=await otherWeb.request(`/Portal/Survey/${surveyId}`); passed('Survey ownership is enforced on MVC too',page.status===404);
page=await web.request('/Portal/Surveys?status=pending'); passed('Pending inbox lists available invitations',htmlText(page.html).includes(draft.title) && htmlText(page.html).includes('Mở khảo sát'));
page=await web.request('/Portal'); passed('Dashboard shows the new invitation',htmlText(page.html).includes(draft.title));
survey=await api('GET',`/api/customer/surveys/${surveyId}`,undefined,token);
const [single,multi,rating,text]=survey.questions;
let fields=[[`q_${single.id}`,single.options[0].id],[`q_${rating.id}`,'5'],[`q_${text.id}`,unsafe]];
page=await web.post(`/Portal/Survey/${surveyId}`,fields,`/Portal/Survey/${surveyId}`);
passed('Server rejects missing required multiple choice and retains text',htmlText(page.html).includes('Thiếu câu bắt buộc') && page.html.includes('&lt;script&gt;'));
fields.push([`q_${multi.id}`,multi.options[0].id],[`q_${multi.id}`,multi.options[1].id]);
page=await web.post(`/Portal/Survey/${surveyId}`,[...fields,[`q_${rating.id}`,'4']],`/Portal/Survey/${surveyId}`);
passed('Forged duplicate rating fields are rejected',htmlText(page.html).includes('chỉ nhận một câu trả lời'));
page=await web.post(`/Portal/Survey/${surveyId}`,[...fields,['q_00000000-0000-0000-0000-000000000000','5']],`/Portal/Survey/${surveyId}`);
passed('Forged unknown question fields are rejected',htmlText(page.html).includes('Câu hỏi không thuộc khảo sát'));
await api('POST',`/api/customer/surveys/${surveyId}/responses`,{answers:[null]},token,400); passed('Null API answer entries are rejected without server failure');
page=await web.post(`/Portal/Survey/${surveyId}`,fields,`/Portal/Survey/${surveyId}`);
passed('Customer submits all four question types once',page.path==='/Portal/Surveys' && htmlText(page.html).includes('Cảm ơn bạn đã trả lời'));
const saved=await api('GET',`/api/customer/surveys/${surveyId}/response`,undefined,token);
passed('Saved answer receipt includes timestamp and original values',saved.answers.length===4 && saved.answers.some(x=>x.questionId===multi.id && x.optionIds.length===2) && saved.answers.some(x=>x.text===unsafe) && !!saved.submittedAtUtc);
page=await web.request(`/Portal/Survey/${surveyId}`);
passed('Completed survey shows encoded read only answers and optional omission',!page.html.includes('id="survey-response"') && page.html.includes('&lt;script&gt;') && htmlText(page.html).includes('Bạn đã bỏ qua câu không bắt buộc'));
page=await web.request('/Portal/Surveys?status=answered'); passed('Answered inbox links to the saved receipt',htmlText(page.html).includes('Xem câu trả lời đã gửi'));
await api('POST',`/api/customer/surveys/${surveyId}/responses`,{answers:[]},token,409); passed('Duplicate survey submission cannot create another response');
const results=await api('GET',`/api/crm/surveys/${surveyId}/results`,undefined,manager);
passed('Survey statistics correctly count participation and multiple selections',results.responded===1 && results.responseRate===100 && results.questions.find(x=>x.questionId===multi.id).options.reduce((n,x)=>n+x.percentage,0)===200);

// Hai POST thật đồng thời phải vẫn chỉ tạo một response cho một invitation.
const raceId=(await api('POST','/api/crm/surveys',{...draft,title:'Khảo sát đồng thời',questions:[draft.questions[2]]},manager)).id;
let race=(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===raceId);
await api('POST',`/api/crm/surveys/${raceId}/publish`,{customerIds:[profile.id],concurrencyToken:race.concurrencyToken},manager);
race=await api('GET',`/api/customer/surveys/${raceId}`,undefined,token);
const raceBody={answers:[{questionId:race.questions[0].id,text:null,rating:4,optionIds:[]}]};
const simultaneous=await Promise.all([1,2].map(()=>fetch(base+`/api/customer/surveys/${raceId}/responses`,{method:'POST',headers:{'Content-Type':'application/json',Authorization:`Bearer ${token}`},body:JSON.stringify(raceBody)})));
const raceStatuses=simultaneous.map(x=>x.status).sort();
passed('Concurrent survey requests accept exactly one response',raceStatuses[0]===200 && raceStatuses[1]===409);
passed('Concurrent requests leave consistent statistics',(await api('GET',`/api/crm/surveys/${raceId}/results`,undefined,manager)).responded===1);
const closedId=(await api('POST','/api/crm/surveys',{...draft,title:'Khảo sát đã đóng'},manager)).id;
let closed=(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===closedId);
await api('POST',`/api/crm/surveys/${closedId}/publish`,{customerIds:[profile.id],concurrencyToken:closed.concurrencyToken},manager);
closed=(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===closedId);
await api('POST',`/api/crm/surveys/${closedId}/close`,closed.concurrencyToken,manager,204);
await api('POST',`/api/customer/surveys/${closedId}/responses`,{answers:[]},token,409);
page=await web.request(`/Portal/Survey/${closedId}`); passed('Closed surveys reject submission and show no input form',!page.html.includes('id="survey-response"'));
page=await web.request('/Portal/Surveys?status=closed'); passed('Closed inbox separates unavailable invitations',htmlText(page.html).includes('Khảo sát đã đóng') && !htmlText(page.html).includes('Mở khảo sát'));
const deadline=new Date(Date.now()+3000).toISOString();
const expiredId=(await api('POST','/api/crm/surveys',{...draft,title:'Khảo sát hết hạn',closesAtUtc:deadline},manager)).id;
const expiring=(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===expiredId);
await api('POST',`/api/crm/surveys/${expiredId}/publish`,{customerIds:[profile.id],concurrencyToken:expiring.concurrencyToken},manager);
await new Promise(resolve=>setTimeout(resolve,Math.max(0,Date.parse(deadline)-Date.now())+100));
await api('POST',`/api/customer/surveys/${expiredId}/responses`,{answers:[]},token,409);
page=await web.request(`/Portal/Survey/${expiredId}`); passed('Expiry alone rejects responses and removes the form',!page.html.includes('id="survey-response"') && htmlText(page.html).includes('hết hạn'));

const secondSession=new Session(); await secondSession.login(email);
page=await web.post('/Portal/ChangePassword',{CurrentPassword:'Wrong@123',NewPassword:newPassword,ConfirmPassword:newPassword},'/Portal/ChangePassword');
passed('Password change requires correct current password',htmlText(page.html).includes('Mật khẩu hiện tại chưa đúng') && !page.html.includes(newPassword));
page=await web.post('/Portal/ChangePassword',{CurrentPassword:password,NewPassword:password,ConfirmPassword:password},'/Portal/ChangePassword');
passed('Password change rejects reusing the current password',htmlText(page.html).includes('phải khác mật khẩu hiện tại'));
page=await web.post('/Portal/ChangePassword',{CurrentPassword:password,NewPassword:newPassword,ConfirmPassword:newPassword},'/Portal/ChangePassword');
passed('Successful password change preserves the active browser session',page.path==='/Portal/Profile' && htmlText(page.html).includes('Đã đổi mật khẩu'));
await api('GET','/api/customer/profile',undefined,token,401); passed('Password change invalidates old API tokens');
await api('POST','/api/auth/refresh',{refreshToken:authTokens.refreshToken},undefined,401); passed('Password change invalidates old refresh tokens');
page=await secondSession.request('/Portal/Profile'); passed('Password change invalidates other browser cookies',page.path==='/Account/Login');
await api('POST','/api/auth/login',{email,password},undefined,401); passed('Old password no longer signs in');
const renewed=await loginApi(email,newPassword); passed('New password signs in successfully',!!renewed);
await api('POST',`/api/crm/customers/${profile.id}/lock`,true,manager,204);
page=await web.request('/Portal/Profile'); passed('Manager lock immediately removes the current customer web session',page.path==='/Account/Login');
await api('POST','/api/auth/login',{email,password:newPassword},undefined,401); passed('Manager locked account cannot sign in');

// Khóa tạm do sai mật khẩu khác với IsDisabled do quản lý đặt.
await api('POST','/api/auth/register',{email:`lockout-${suffix}@cafe.test`,password,fullName:'Khách thử khóa tạm'},undefined,201);
for(let i=0;i<5;i++) await api('POST','/api/auth/login',{email:`lockout-${suffix}@cafe.test`,password:'Wrong@123'},undefined,401);
await api('POST','/api/auth/login',{email:`lockout-${suffix}@cafe.test`,password},undefined,401); passed('Five wrong passwords trigger temporary account lockout');

// Exhaust the web rate budget last so it cannot interfere with functional checks above.
let limited;
for(let i=0;i<25;i++) {
    limited=await guest.post('/Account/Login',{email:'missing@cafe.test',password},'/Account/Login');
    if(limited.status===429) break;
}
passed('Authentication forms are rate limited with Retry-After',limited.status===429 && limited.headers.get('retry-after')==='60');
const summary={verifiedAtUtc:new Date().toISOString(),base,checks:checks.length,passed:checks,customerEmail:email,surveyId};
if(process.argv[3]) await writeFile(process.argv[3],JSON.stringify(summary,null,2)+'\n');
console.log(`Verified ${checks.length} customer workflow checks.`);
