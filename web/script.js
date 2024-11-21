const DEBUG = true;
var currentResponseDiv = null;
var messageBuffer = '';
var updateScheduled = false;
var config = null;

window.initializeUI = function(serverConfig) {
    if (DEBUG) console.log('Initializing UI with config:', serverConfig);
    config = serverConfig;
    
    const modelSelect = document.getElementById('model-select');
    const maxTokens = document.getElementById('max-tokens');
    const maxTokensValue = document.getElementById('max-tokens-value');
    const userInput = document.getElementById('user-input');

    if (config.models) {
        modelSelect.innerHTML = Object.entries(config.models)
            .map(([value, model]) => `
                <option value="${value}">${model.name}</option>
            `)
            .join('');
    }

    modelSelect.value = config.ui.defaultModel;
    if (DEBUG) console.log('Setting default model to:', config.ui.defaultModel);
    
    maxTokens.setAttribute('min', config.ui.minTokens);
    maxTokens.setAttribute('max', config.ui.maxTokens);
    maxTokens.setAttribute('step', config.ui.tokenStep);
    maxTokens.value = config.ui.defaultMaxTokens;
    maxTokensValue.textContent = maxTokens.value;

    setTimeout(() => userInput.focus(), 100);
}

document.addEventListener('DOMContentLoaded', () => {
    const chatHistory = document.getElementById('chat-history');
    const userInput = document.getElementById('user-input');
    const sendButton = document.getElementById('send-button');
    const maxTokens = document.getElementById('max-tokens');
    const maxTokensValue = document.getElementById('max-tokens-value');

    maxTokens.addEventListener('input', (e) => {
        maxTokensValue.textContent = e.target.value;
    });

    const sendMessage = () => {
        const message = userInput.value.trim();
        if (!message) return;
        
        console.log('Creating message divs');
        addMessageToChat('user', message);
        
        // Create response div with explicit logging
        currentResponseDiv = document.createElement('div');
        currentResponseDiv.className = 'message assistant-message loading';
        console.log('Created response div:', currentResponseDiv);
        
        const chatHistory = document.getElementById('chat-history');
        chatHistory.appendChild(currentResponseDiv);
        console.log('Appended response div');
        
        messageBuffer = '';
        currentResponseDiv = document.createElement('div');
        currentResponseDiv.className = 'message assistant-message loading';
        chatHistory.appendChild(currentResponseDiv);
        
        const modelSelect = document.getElementById('model-select');
        const includeKeystrokes = document.getElementById('include-keystrokes');
        
        window.chrome.webview.postMessage({
            type: 'sendMessage',
            message: message,
            model: modelSelect.value,
            includeKeystrokes: includeKeystrokes.checked,
            maxTokens: parseInt(maxTokens.value)
        });

        userInput.value = '';
        userInput.style.height = 'auto';
        chatHistory.scrollTop = chatHistory.scrollHeight;
    };

    sendButton.addEventListener('click', sendMessage);

    userInput.addEventListener('keydown', (e) => {
        if (e.ctrlKey && e.key === 'Enter') {
            e.preventDefault();
            sendMessage();
        }
    });

    userInput.addEventListener('input', () => {
        userInput.style.height = 'auto';
        userInput.style.height = Math.min(userInput.scrollHeight, 200) + 'px';
    });
});

function addMessageToChat(role, content) {
    const chatHistory = document.getElementById('chat-history');
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${role}-message`;
    messageDiv.textContent = content;
    chatHistory.appendChild(messageDiv);
    chatHistory.scrollTop = chatHistory.scrollHeight;
}

function isNearBottom(element) {
    return element.scrollHeight - element.scrollTop - element.clientHeight < 100;
}

function receiveChunk(text) {
    console.log('receiveChunk called with:', text);
    console.log('currentResponseDiv:', currentResponseDiv);
    
    if (!currentResponseDiv) {
        console.error('No response div available');
        return;
    }
    
    messageBuffer += text;
    
    if (!updateScheduled) {
        updateScheduled = true;
        requestAnimationFrame(function() {
            console.log('Updating div with text:', messageBuffer);
            if (currentResponseDiv) {
                currentResponseDiv.textContent = messageBuffer;
                currentResponseDiv.classList.remove('loading');
                
                var chatHistory = document.getElementById('chat-history');
                if (isNearBottom(chatHistory)) {
                    chatHistory.scrollTop = chatHistory.scrollHeight;
                }
            }
            updateScheduled = false;
        });
    }
}

window.receiveChunk = receiveChunk;

window.finishStream = function(cost) {
    if (DEBUG) console.log('Stream finished, cost:', cost);
    
    if (!currentResponseDiv) {
        console.error('No response div available for final update');
        return;
    }
    
    currentResponseDiv.textContent = messageBuffer;
    currentResponseDiv.classList.remove('loading');
    
    if (cost !== undefined && cost !== null) {
        const costDiv = document.createElement('div');
        costDiv.className = 'cost';
        costDiv.textContent = `Cost: $${cost.toFixed(4)}`;
        currentResponseDiv.appendChild(costDiv);
    }
    
    const chatHistory = document.getElementById('chat-history');
    chatHistory.scrollTop = chatHistory.scrollHeight;
    
    currentResponseDiv = null;
    messageBuffer = '';
    updateScheduled = false;
};

window.handleError = function(errorMessage) {
    if (DEBUG) console.log('Error:', errorMessage);
    
    const chatHistory = document.getElementById('chat-history');
    const errorDiv = document.createElement('div');
    errorDiv.className = 'message error-message';
    errorDiv.textContent = `Error: ${errorMessage}`;
    chatHistory.appendChild(errorDiv);
    chatHistory.scrollTop = chatHistory.scrollHeight;
    
    if (currentResponseDiv) {
        currentResponseDiv.remove();
    }
    currentResponseDiv = null;
    messageBuffer = '';
    updateScheduled = false;
};