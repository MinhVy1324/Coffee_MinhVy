(() => {
    'use strict';
    // HTML required trên từng checkbox sẽ bắt chọn tất cả. Kiểm tra nhóm để chỉ yêu cầu ít nhất một.
    // Service vẫn kiểm tra lại ở server khi JavaScript bị tắt hoặc người dùng tự gửi request.
    document.querySelectorAll('[data-min-selection="1"]').forEach(group => {
        const inputs = [...group.querySelectorAll('input[type="checkbox"]')];
        const validate = () => inputs[0]?.setCustomValidity(inputs.some(input => input.checked) ? '' : 'Chọn ít nhất một đáp án cho câu hỏi này.');
        group.addEventListener('change', validate);
        validate();
    });
})();
