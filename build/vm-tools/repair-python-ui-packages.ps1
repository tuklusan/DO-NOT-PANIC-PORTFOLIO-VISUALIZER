$ErrorActionPreference = 'Continue'

if (Get-Command py -ErrorAction SilentlyContinue) {
    py -m pip install --upgrade --force-reinstall pytweening
    py -m pip install --upgrade --force-reinstall pyautogui
    py -m pip show pytweening pyautogui
} else {
    Write-Host "Python launcher 'py' not found."
}
