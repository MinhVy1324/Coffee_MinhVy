(() => {
    'use strict';
    const form = document.getElementById('survey-editor');
    if (!form) return;
    const container = document.getElementById('questions');
    const template = document.getElementById('question-template');
    const add = document.getElementById('addQuestion');
    const hint = document.getElementById('question-editor-hint');

    const updateKind = question => {
        const kind = question.querySelector('[data-field="Kind"]').value;
        const choice = kind === 'SingleChoice' || kind === 'MultipleChoice';
        question.querySelector('[data-choice-options]').hidden = !choice;
        const options = question.querySelector('[data-field="Options"]');
        options.required = choice;
        options.disabled = !choice;
        question.querySelector('[data-kind-hint]').textContent = kind === 'Rating' ? 'Khách chấm điểm từ 1 đến 5.' : kind === 'Text' ? 'Khách nhập góp ý, tối đa 2000 ký tự.' : '';
    };
    const refresh = () => {
        const questions = [...container.querySelectorAll('[data-question]')];
        // MVC cần chỉ số liên tiếp: sau khi xóa câu 2, đổi tên câu 3 thành Questions[1].
        // Nếu không đánh lại chỉ số, model binder có thể bỏ mất các câu phía sau chỗ trống.
        questions.forEach((question, index) => {
            question.querySelector('[data-question-title]').textContent = `Câu ${index + 1}`;
            question.querySelectorAll('[data-field]').forEach(field => {
                field.name = `Questions[${index}].${field.dataset.field}`;
                if (field.id) {
                    const label = question.querySelector(`label[for="${field.id}"]`);
                    field.id = `question-${index}-${field.dataset.field.toLowerCase()}`;
                    if (label) label.htmlFor = field.id;
                }
            });
            const remove = question.querySelector('.remove-question');
            remove.hidden = false;
            remove.disabled = questions.length <= 1;
            remove.setAttribute('aria-label', `Xóa câu ${index + 1}`);
            updateKind(question);
        });
        document.getElementById('question-count').textContent = `${questions.length} / 30 câu`;
        add.disabled = questions.length >= 30;
        hint.textContent = questions.length >= 30 ? 'Đã đạt giới hạn 30 câu hỏi.' : 'Khảo sát cần ít nhất một câu hỏi.';
    };
    container.addEventListener('change', event => {
        if (event.target.dataset.field === 'Kind') updateKind(event.target.closest('[data-question]'));
    });
    container.addEventListener('click', event => {
        const remove = event.target.closest('.remove-question');
        if (!remove || container.querySelectorAll('[data-question]').length <= 1) return;
        remove.closest('[data-question]').remove();
        refresh();
        add.focus();
    });
    add.hidden = false;
    add.addEventListener('click', () => {
        if (container.querySelectorAll('[data-question]').length >= 30) return;
        container.append(template.content.cloneNode(true));
        refresh();
        container.lastElementChild.querySelector('[data-field="Text"]').focus();
    });
    refresh();
})();
