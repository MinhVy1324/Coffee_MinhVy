(() => {
    'use strict';
    const form = document.getElementById('publish-survey');
    if (!form) return;
    const picker = document.getElementById('recipient-picker');
    const checkboxes = [...picker.querySelectorAll('[name="customerIds"]')];
    const update = () => {
        const selected = form.querySelector('[name="audience"]:checked')?.value === 'selected';
        picker.hidden = !selected;
        // Không gửi customerIds trong chế độ tất cả; server cũng kiểm tra audience riêng.
        checkboxes.forEach(input => { input.disabled = !selected; });
        document.getElementById('recipient-count').textContent = `${checkboxes.filter(input => input.checked).length} đã chọn`;
    };
    form.addEventListener('change', update);
    document.getElementById('recipient-search').addEventListener('input', event => {
        const query = event.target.value.trim().toLocaleLowerCase('vi');
        const rows = [...picker.querySelectorAll('[data-recipient]')];
        rows.forEach(row => { row.hidden = !row.textContent.toLocaleLowerCase('vi').includes(query); });
        document.getElementById('recipient-empty').hidden = rows.some(row => !row.hidden);
    });
    update();
})();
