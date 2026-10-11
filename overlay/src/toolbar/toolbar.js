'use strict';

const opacityInput = document.getElementById('opacity');
const opacityValue = document.getElementById('opacity-value');
const modeSelect = document.getElementById('mode');
const clickThroughButton = document.getElementById('click-through');

function render(state) {
    if (!state) {
        return;
    }

    const percent = Math.round(state.opacity * 100);

    opacityInput.value = String(percent);
    opacityValue.textContent = `${percent}%`;
    modeSelect.value = state.mode;
    clickThroughButton.classList.toggle('active', state.clickThrough);
}

document.querySelectorAll('button[data-action]').forEach(button => {
    button.addEventListener('click', () => {
        window.overlay.action(button.dataset.action);
    });
});

opacityInput.addEventListener('input', () => {
    opacityValue.textContent = `${opacityInput.value}%`;
    window.overlay.action('set-opacity', Number(opacityInput.value) / 100);
});

modeSelect.addEventListener('change', () => {
    window.overlay.action('set-mode', modeSelect.value);
});

window.overlay.onState(render);
window.overlay.getState().then(render);
