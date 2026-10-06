(() => {
    'use strict';
    document.documentElement.classList.add('js');
    document.querySelectorAll('form[data-confirm]').forEach(form => {
        form.addEventListener('submit', event => {
            if (!window.confirm(form.dataset.confirm)) event.preventDefault();
        });
    });

    const menuToggle = document.getElementById('menu-toggle');
    const sidebar = document.getElementById('sidebar');
    const setNavigation = (open) => {
        document.body.classList.toggle('nav-open', open);
        menuToggle?.setAttribute('aria-expanded', String(open));
        menuToggle?.setAttribute('aria-label', open ? 'Đóng menu' : 'Mở menu');
        if (open) sidebar?.querySelector('.nav-link.active, .nav-link')?.focus();
    };
    menuToggle?.addEventListener('click', () => setNavigation(!document.body.classList.contains('nav-open')));
    document.querySelector('[data-close-nav]')?.addEventListener('click', () => {
        setNavigation(false);
        menuToggle?.focus();
    });
    sidebar?.querySelectorAll('.nav-link').forEach(link => link.addEventListener('click', () => setNavigation(false)));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            if (document.body.classList.contains('nav-open')) {
                setNavigation(false);
                menuToggle?.focus();
            }
            document.querySelectorAll('.account-menu[open]').forEach(menu => menu.removeAttribute('open'));
        }
    });
    window.matchMedia('(min-width: 901px)').addEventListener('change', event => {
        if (event.matches) setNavigation(false);
    });
    document.addEventListener('click', event => {
        document.querySelectorAll('.account-menu[open]').forEach(menu => {
            if (!menu.contains(event.target)) menu.removeAttribute('open');
        });
    });

    document.querySelectorAll('[data-password-toggle]').forEach(button => {
        const input = document.getElementById(button.dataset.passwordToggle);
        if (!input) return;
        button.hidden = false;
        button.addEventListener('click', () => {
            const visible = input.type === 'password';
            input.type = visible ? 'text' : 'password';
            button.setAttribute('aria-pressed', String(visible));
            button.setAttribute('aria-label', visible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
            button.querySelector('use')?.setAttribute('href', visible ? '/img/icons.svg#eye-off' : '/img/icons.svg#eye');
        });
    });

    // So khớp mật khẩu chỉ giúp phát hiện lỗi ngay khi nhập. [Compare] ở server vẫn bắt buộc.
    document.querySelectorAll('[data-match-password]').forEach(input => {
        const source = document.getElementById(input.dataset.matchPassword);
        const validate = () => input.setCustomValidity(input.value && input.value !== source?.value ? 'Hai mật khẩu chưa trùng nhau.' : '');
        input.addEventListener('input', validate);
        source?.addEventListener('input', validate);
        validate();
    });
    document.querySelectorAll('[data-character-count]').forEach(input => {
        const output = document.getElementById(input.dataset.characterCount);
        const update = () => { if (output) output.textContent = `${input.value.length.toLocaleString('vi-VN')} / ${input.maxLength.toLocaleString('vi-VN')} ký tự`; };
        input.addEventListener('input', update);
        update();
    });
    // Chống bấm liên tiếp ở giao diện; survey còn được bảo vệ bằng unique index ở database.
    document.querySelectorAll('form[data-prevent-double-submit]').forEach(form => {
        form.addEventListener('submit', event => {
            if (event.defaultPrevented) return;
            if (form.dataset.submitting === 'true') { event.preventDefault(); return; }
            form.dataset.submitting = 'true';
            form.querySelectorAll('[type="submit"]').forEach(button => { button.disabled = true; });
            form.setAttribute('aria-busy', 'true');
        });
    });
    // Trình duyệt có thể phục hồi trang cũ từ bfcache khi bấm Back, cần mở lại nút gửi.
    window.addEventListener('pageshow', () => document.querySelectorAll('form[data-prevent-double-submit]').forEach(form => {
        delete form.dataset.submitting;
        form.removeAttribute('aria-busy');
        form.querySelectorAll('[type="submit"]').forEach(button => { button.disabled = false; });
    }));

    document.querySelectorAll('.card > table').forEach(table => {
        const container = document.createElement('div');
        container.className = 'table-scroll';
        container.tabIndex = 0;
        container.setAttribute('role', 'region');
        container.setAttribute('aria-label', 'Bảng dữ liệu, cuộn ngang để xem thêm');
        table.before(container);
        container.append(table);
    });
})();
