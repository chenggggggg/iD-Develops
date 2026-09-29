function updateSpeedometer(score, maxScore) {
    const percentage = (score / maxScore) * 100;
    const degrees = (percentage / 100) * 240; // 240 degrees for the 2/3 circle
    document.getElementById('speedometer').style.setProperty('--percentage', degrees);
}

document.addEventListener('DOMContentLoaded', (event) => {
    const scoreText = document.getElementById('score-text').innerText;
    const [score, maxScore] = scoreText.split('/').map(Number);
    updateSpeedometer(score, maxScore);
});
