// Kiểm tra tích hợp bằng Node.js, không cần npm/dependency.
// Chạy với database demo KIỂM THỬ riêng: node tests/crm-workflows.mjs http://127.0.0.1:5261
// Tạo các tài khoản tổng hợp, khóa và lưu trữ chính những khách thử nghiệm đó.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
const base = process.argv[2] || 'http://127.0.0.1:5261';
const checks = [];
const unique = Date.now().toString(36);
const password = 'CafeDemo@123';
const decodeHtml = html => html.replace(/&#x([0-9a-f]+);/gi,(_,code)=>String.fromCodePoint(parseInt(code,16)))
    .replace(/&#([0-9]+);/g,(_,code)=>String.fromCodePoint(Number(code)))
    .replace(/&quot;/g,'"').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&amp;/g,'&');
function passed(name, condition = true) { assert.ok(condition, name); checks.push(name); console.log('PASS', name); }
async function api(method, path, body, token, expected = 200) {
    const response = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
    const raw = await response.text();
    assert.equal(response.status, expected, `${method} ${path}: ${raw.slice(0,300)}`);
    return raw ? JSON.parse(raw) : null;
}
const token = async email => (await api('POST', '/api/auth/login', { email, password })).accessToken;
class WebSession {
    cookies = new Map();
    async request(path, options = {}) {
        for (let i = 0; i < 8; i++) {
            const response = await fetch(base + path, { ...options, redirect: 'manual', headers: { ...options.headers, Cookie: [...this.cookies].map(([key,value]) => `${key}=${value}`).join('; ') } });
            for (const cookie of response.headers.getSetCookie()) {
                const part = cookie.split(';')[0]; const split = part.indexOf('=');
                const name = part.slice(0,split); const value = part.slice(split + 1);
                if (value) this.cookies.set(name,value); else this.cookies.delete(name);
            }
            if ([301,302,303].includes(response.status)) {
                const destination = new URL(response.headers.get('location'),base);
                assert.equal(destination.origin,new URL(base).origin,'Redirect must remain on the local application');
                path = destination.pathname + destination.search;
                options = {};
                continue;
            }
            return { status: response.status, html: await response.text(), path };
        }
        throw new Error('Too many redirects');
    }
    async post(path, fields, tokenPath) {
        const page = await this.request(tokenPath);
        const csrf = page.html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
        assert.ok(csrf, `Missing CSRF form on ${tokenPath}`);
        const data = new URLSearchParams(Array.isArray(fields) ? fields : Object.entries(fields));
        data.set('__RequestVerificationToken',csrf);
        return this.request(path,{ method: 'POST', body: data });
    }
    async login(email) { return this.post('/Account/Login',{email,password},'/Account/Login'); }
}

const manager = await token('manager@cafe.test');
const staff = await token('staff@cafe.test');
const customer = await token('customer@cafe.test');
const managerWeb = new WebSession(); await managerWeb.login('manager@cafe.test');
const staffWeb = new WebSession(); await staffWeb.login('staff@cafe.test');
const customerWeb = new WebSession(); await customerWeb.login('customer@cafe.test');

// 1. Nhân viên tạo khách trên WEB; server tạo cả tài khoản và hồ sơ, chuyển sang bổ sung thông tin.
const email = `workflow-${unique}@cafe.test`;
let page = await staffWeb.post('/Crm/CreateCustomer',{Email: email, FullName: 'Khách kiểm thử', InitialPassword: password},'/Crm/CreateCustomer');
passed('Staff creates a customer from the web', page.status === 200 && page.path.startsWith('/Crm/EditCustomer/'));
let profile = (await api('GET','/api/crm/customers',undefined,manager)).find(x => x.email === email);
const id = profile.id;
page = await staffWeb.post('/Crm/CreateCustomer',{Email: email, FullName: 'Tên được giữ lại', InitialPassword: password},'/Crm/CreateCustomer');
passed('Duplicate customer keeps name/email and shows a form error', page.html.includes('validation-summary-errors') && page.html.includes(email) && decodeHtml(page.html).includes('value="Tên được giữ lại"'));
passed('Customer password is not returned in HTML', !page.html.includes(`value="${password}"`));
const newCustomer = await token(email);
const preferences = await api('GET','/api/customer/preferences',undefined,newCustomer);
page = await staffWeb.post(`/Crm/EditCustomer/${id}`,{FullName: 'Khách kiểm thử', Phone: '', BirthDate: '2000-01-01', Address: 'Hồ Chí Minh', PreferenceIds: String(preferences[0].id), ConcurrencyToken: profile.concurrencyToken},`/Crm/EditCustomer/${id}`);
passed('Staff updates birth date and preferences from the web', page.path === '/Crm/Customers');
let ownProfile = await api('GET','/api/customer/profile',undefined,newCustomer);
passed('Customer sees saved preferences', ownProfile.preferenceIds.includes(preferences[0].id));
await api('PUT',`/api/crm/customers/${id}`,{fullName:'Sai phiên bản',phone:null,birthDate:null,address:null,preferenceIds:[],concurrencyToken:profile.concurrencyToken},staff,409);
passed('Stale customer updates are rejected');

// 2–3. Gọi thẳng các URL để chứng minh ẩn nút không phải lớp bảo vệ duy nhất.
for (const path of ['/Crm/Reports','/Crm/Surveys','/Crm/CreateSurvey']) {
    page = await staffWeb.request(path); passed(`Staff cannot open ${path}`,page.status === 403);
}
page = await staffWeb.post('/Crm/LockCustomer',{id,disabled:'true'},'/Crm/Customers'); passed('Staff cannot lock a customer by forging a form',page.status === 403);
page = await staffWeb.post('/Crm/ArchiveCustomer',{id,version:ownProfile.concurrencyToken},'/Crm/Customers'); passed('Staff cannot delete a customer by forging a form',page.status === 403);
for (const [method,path,body] of [['POST',`/api/crm/customers/${id}/lock`,true],['DELETE',`/api/crm/customers/${id}?version=${ownProfile.concurrencyToken}`,undefined],['GET','/api/crm/reports',undefined],['GET','/api/crm/surveys',undefined]]) {
    await api(method,path,body,staff,403); passed(`Staff API permission: ${method} ${path.split('?')[0]}`);
}
await api('GET','/api/crm/customers',undefined,customer,403); passed('Customer cannot read other customer profiles');
page = await managerWeb.post('/Crm/LockCustomer',{id,disabled:'true'},'/Crm/Customers'); passed('Manager locks a customer from the web',page.status === 200);
await api('GET','/api/customer/profile',undefined,newCustomer,401); passed('Lock immediately invalidates existing tokens');
page = await managerWeb.post('/Crm/LockCustomer',{id,disabled:'false'},'/Crm/Customers'); passed('Manager unlocks a customer from the web',page.status === 200);
await api('GET','/api/customer/profile',undefined,newCustomer,401); passed('Unlock does not restore old tokens');
const renewedCustomer = await token(email);

// 4. Khách góp ý, nhân viên xử lý, khách thấy trả lời. Nội dung nguy hiểm được mã hóa.
const guestWeb = new WebSession(); await guestWeb.login(email);
page = await guestWeb.post('/Portal/Feedback',{productId:'',rating:'5',content:'<script>alert(1)</script> Cà phê ngon'},'/Portal/Feedback');
passed('Customer submits feedback from the web',page.status === 200 && page.html.includes('&lt;script&gt;'));
let feedback = (await api('GET','/api/customer/feedback',undefined,renewedCustomer))[0];
page = await staffWeb.post('/Crm/Reply',{id:feedback.id,content:'Cảm ơn bạn, quán đã ghi nhận.',status:'Resolved',concurrencyToken:feedback.concurrencyToken},'/Crm/Feedback');
passed('Staff replies and resolves feedback from the web',page.status === 200);
page = await guestWeb.request('/Portal/Feedback'); passed('Customer sees the staff reply',decodeHtml(page.html).includes('Cảm ơn bạn, quán đã ghi nhận.'));
await api('POST',`/api/crm/feedback/${feedback.id}/replies`,{content:'Phiên bản cũ',status:2,concurrencyToken:feedback.concurrencyToken},staff,409); passed('Stale feedback replies are rejected');

// 5. Báo cáo tuổi gồm nhóm chưa cung cấp ngày sinh; mẫu số chỉ gồm khách hoạt động.
let report = await api('GET','/api/crm/reports',undefined,manager);
passed('Age groups sum to the active customer count',report.ageGroups.reduce((sum,x) => sum + x.count,0) === report.activeCustomers);
passed('Age report includes unknown birth dates',report.ageGroups.some(x => x.group === 'Chưa có ngày sinh'));
passed('Preferences reflect the customer profile',report.preferences.some(x => x.name === preferences[0].name && x.count > 0));
page = await managerWeb.request('/Crm/Reports'); passed('Manager report renders real charts',page.html.includes('chart-bar') && page.html.includes('Cơ cấu độ tuổi'));

// 6–7. Tạo đủ bốn loại câu hỏi qua web; gửi có chọn người nhận và thống kê câu trả lời.
const title = `Khảo sát workflow ${unique}`;
const deadline = new Date(Date.now() + 7*86400000 + 7*3600000).toISOString().slice(0,16);
const fields = {Title:title,Description:'Khảo sát kiểm thử',ClosesAt:deadline,'Questions[0].Text':'Món yêu thích?','Questions[0].Kind':'SingleChoice','Questions[0].IsRequired':'true','Questions[0].Options':'Cold brew\nLatte','Questions[1].Text':'Sở thích?','Questions[1].Kind':'MultipleChoice','Questions[1].IsRequired':'true','Questions[1].Options':'Ít đường\nNhiều đá','Questions[2].Text':'Mức hài lòng?','Questions[2].Kind':'Rating','Questions[2].IsRequired':'true','Questions[3].Text':'Góp ý thêm?','Questions[3].Kind':'Text','Questions[3].IsRequired':'false'};
page = await managerWeb.post('/Crm/CreateSurvey',fields,'/Crm/CreateSurvey'); passed('Manager creates four question types from the web',page.path === '/Crm/Surveys');
let survey = (await api('GET','/api/crm/surveys',undefined,manager)).find(x => x.title === title);
assert.ok(survey);
const sid = survey.id;
page = await staffWeb.request(`/Crm/PublishSurvey/${sid}`); passed('Staff cannot access recipient selection',page.status === 403);
page = await staffWeb.request(`/Crm/Results/${sid}`); passed('Staff cannot access survey results',page.status === 403);
await api('POST','/api/crm/surveys',{title:'Không có quyền',closesAtUtc:new Date(Date.now()+86400000).toISOString(),questions:[]},staff,403); passed('Staff cannot create surveys through the API');
await api('POST',`/api/crm/surveys/${sid}/publish`,{customerIds:[id],concurrencyToken:survey.concurrencyToken},staff,403); passed('Staff cannot publish surveys through the API');
await api('GET',`/api/crm/surveys/${sid}/results`,undefined,staff,403); passed('Staff cannot view results through the API');
page = await managerWeb.post('/Crm/Publish',{id:sid,version:survey.concurrencyToken,audience:'selected'},`/Crm/PublishSurvey/${sid}`);
passed('Selecting zero recipients shows a form error',page.html.includes('validation-summary-errors'));
passed('Zero recipients does not accidentally publish to everyone',(await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===sid).status === 0);
await api('POST',`/api/crm/surveys/${sid}/publish`,{customerIds:[],concurrencyToken:survey.concurrencyToken},manager,400); passed('API also rejects an empty recipient list');
page = await managerWeb.post('/Crm/Publish',{id:sid,version:survey.concurrencyToken,audience:'selected',customerIds:id},`/Crm/PublishSurvey/${sid}`);
passed('Manager sends a targeted survey from the web',page.path === '/Crm/Surveys');
let inbox = await api('GET','/api/customer/surveys',undefined,renewedCustomer); passed('Selected customer receives the survey',inbox.some(x=>x.surveyId===sid));
passed('Unselected customer does not receive the survey',!(await api('GET','/api/customer/surveys',undefined,customer)).some(x=>x.surveyId===sid));
await api('GET',`/api/customer/surveys/${sid}`,undefined,customer,404); passed('Uninvited customer cannot access survey URL');
survey = (await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===sid);
const repeated = await api('POST',`/api/crm/surveys/${sid}/publish`,{customerIds:[id],concurrencyToken:survey.concurrencyToken},manager); passed('Sending again does not duplicate invitations',repeated.sent === 0);
const [single,multiple,rating,text] = survey.questions;
page = await guestWeb.post(`/Portal/Survey/${sid}`,{[`q_${rating.id}`]:'5'},`/Portal/Survey/${sid}`); passed('Required answers are checked by the server',page.html.includes('validation-summary-errors'));
page = await guestWeb.post(`/Portal/Survey/${sid}`,[[`q_${single.id}`,single.options[0].id],[`q_${multiple.id}`,multiple.options[0].id],[`q_${multiple.id}`,multiple.options[1].id],[`q_${rating.id}`,'5'],[`q_${text.id}`,'<b>Rất hài lòng</b>']],`/Portal/Survey/${sid}`);
passed('Customer answers all question types from the web',page.path === '/Portal/Surveys');
let result = await api('GET',`/api/crm/surveys/${sid}/results`,undefined,manager);
passed('Participation statistics use actual invitations',result.invited === 1 && result.responded === 1 && result.responseRate === 100);
passed('Multiple choice percentages may correctly total 200%',result.questions[1].options.reduce((sum,x)=>sum+x.percentage,0)===200);
passed('Average survey rating is correct',result.questions[2].averageRating===5);
page = await managerWeb.request(`/Crm/Results/${sid}`); passed('Text survey answers are HTML encoded',page.html.includes('&lt;b&gt;') && decodeHtml(page.html).includes('<b>Rất hài lòng</b>') && !page.html.includes('<b>Rất hài lòng</b>'));
await api('POST',`/api/customer/surveys/${sid}/responses`,{answers:[]},renewedCustomer,409); passed('Duplicate responses are rejected');
page = await guestWeb.request(`/Portal/Survey/${sid}`); passed('Answered survey does not show another submission form',!page.html.includes('id="survey-response"'));
survey = (await api('GET','/api/crm/surveys',undefined,manager)).find(x=>x.id===sid);
page = await managerWeb.post('/Crm/CloseSurvey',{id:sid,version:survey.concurrencyToken},'/Crm/Surveys'); passed('Manager closes the survey from the web',page.path==='/Crm/Surveys');
inbox = await api('GET','/api/customer/surveys',undefined,renewedCustomer); passed('Customer inbox exposes closed survey state',inbox.find(x=>x.surveyId===sid).isClosed === true);
const blocked = await managerWeb.request('/Crm/ArchiveCustomer',{method:'POST',body:new URLSearchParams({id,version:ownProfile.concurrencyToken})}); passed('Mutation without CSRF is rejected',blocked.status===400);

// Xóa khách thử nghiệm sau cùng; chứng minh dữ liệu lịch sử vẫn tồn tại.
ownProfile = await api('GET','/api/customer/profile',undefined,renewedCustomer);
const todayInVietnam = new Date(Date.now()+7*3600000).toISOString().slice(0,10);
await api('PUT',`/api/crm/customers/${id}`,{fullName:ownProfile.fullName,phone:ownProfile.phone,birthDate:todayInVietnam,address:ownProfile.address,preferenceIds:ownProfile.preferenceIds,concurrencyToken:ownProfile.concurrencyToken},staff,204);
passed('Birthday validation accepts today in the cafe timezone');
ownProfile = await api('GET','/api/customer/profile',undefined,renewedCustomer);
await api('PUT',`/api/crm/customers/${id}`,{fullName:ownProfile.fullName,phone:ownProfile.phone,birthDate:'2999-01-01',address:ownProfile.address,preferenceIds:ownProfile.preferenceIds,concurrencyToken:ownProfile.concurrencyToken},staff,400);
passed('Future birthdays are rejected');
page = await managerWeb.post('/Crm/ArchiveCustomer',{id,version:ownProfile.concurrencyToken},'/Crm/Customers'); passed('Manager deletes/archives a customer from the web',page.path==='/Crm/Customers');
passed('Archived customer leaves the active list',!(await api('GET','/api/crm/customers',undefined,manager)).some(x=>x.id===id));
passed('Archived customer appears in archive list',(await api('GET','/api/crm/customers?archived=true',undefined,manager)).some(x=>x.id===id));
await api('GET','/api/customer/profile',undefined,renewedCustomer,401); passed('Archived customer cannot keep using their token');
passed('Archiving preserves feedback history',(await api('GET','/api/crm/feedback',undefined,manager)).some(x=>x.id===feedback.id));
result = await api('GET',`/api/crm/surveys/${sid}/results`,undefined,manager); passed('Archiving preserves survey results',result.responded===1);
const summary = {passed:checks.length,checks,scope:'Web MVC and REST API on an isolated SQLite Development database.'};
console.log(JSON.stringify(summary,null,2));
if (process.argv[3]) await writeFile(process.argv[3],JSON.stringify(summary,null,2));
