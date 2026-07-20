using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using KModkit;
using System.Linq;


/* Rule Seed Support:
 * 
 * Only change the Sword's Shackle Order
 * This is gonna be done at runtime to avoid messing with all that messy Serialization business:
 * On Puzzle Initialization, gather the Shackles Order, shuffle them according to the ruleseed rules, write them back
 */



public class ScorchingAlchemist : MonoBehaviour, ISerializationCallbackReceiver {

    enum moduleState { Shackles, Heart, Solved};

    [SerializeField] KMBombInfo bombInfo;
    [SerializeField] KMBombModule thisModule;
    [SerializeField] KMAudio moduleAudio;
    [SerializeField] KMRuleSeedable ruleseedManager;
    int ruleseedSeed;
    MonoRandom ruleseedRandom;

    // Mesh & Object References
	[SerializeField] GameObject[] shackles;
	[SerializeField] GameObject wingsLeft, wingsRight;
	[SerializeField] GameObject tailSegmentFirst, tailSegmentSecond, tailSegmentThird;
	[SerializeField] GameObject tailEnd;
	[SerializeField] GameObject shield;
	[SerializeField] GameObject heart;
	[SerializeField] GameObject[] monographyPlatforms;
	[SerializeField] GameObject gearLeft, gearRight;
    [SerializeField] MeshRenderer swordSprite;

    [SerializeField] KMSelectable shackleSelectable1, shackleSelectable2, shackleSelectable3, shackleSelectable4, heartSelectable;


    // Mesh & Object Transform Data
    [SerializeField] float shacklesRotationSpeed, gearRotationSpeed;
    bool haltGearRotationDueToStrike;

    [SerializeField] float tailVerticalRotationFrequency, tailVerticalRotationAmplitude, tailHorizontalRotationFrequency, tailHorizontalRotationAmplitude;
    Vector3 tailSegmentFirstRotation = new Vector3(0, 0, 0);
    Vector3 tailSegmentMiddleRotation = new Vector3(-60, 0, 0);
    Vector3 tailEndRotation;

    [SerializeField] float wingsRotationFrequency, wingsRotationAmplitude;
    Vector3 wingsLeftStartingRotation = new Vector3(-20, -5, -90);
    Vector3 wingsRightStartingRotation = new Vector3(-20, 5, -90);

    [SerializeField] float heartDeflateSpeed;

    Vector3[] MonographyPlatformsStartingTransforms;


    // Puzzle Data
    bool isTailHorizontal;
    moduleState currentModuleState;
    bool isLeftGearTurningUp, isRightGearTurningUp;
    int gearRotationFactor;

    [System.Serializable] public struct Sword
    {
        public string name;
        public Texture texture;
        public string shacklesOrder;
    };

    public List<Sword> allSwords = new List<Sword>();
    Sword startingSword;
    Sword finalSword;

    int indexInAllSwordsTable;
    string startingSwordName;

    int numberOfMonographyPlatforms;
    int targetNumberOfPressesOnHeart;
    int currentNumberOfShacklesPressed;

    int currentNumberOfHeartPresses;
    Coroutine heartCooldownCoroutine;


    // Sounds
    [SerializeField] AudioClip ShackleSound, MonographyPlatformsSound, HeartPressSound, SolveSound;


    // Logging Data
    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;



    // Buttons gathering and GetComponents
    void Awake()
    {
        currentModuleState = moduleState.Shackles;

        // Initialize Logging
        moduleId = moduleIdCounter++;


        shackleSelectable1.OnInteract += delegate () { ShacklesGetsPressed(1); return false; };
        shackleSelectable2.OnInteract += delegate () { ShacklesGetsPressed(2); return false; };
        shackleSelectable3.OnInteract += delegate () { ShacklesGetsPressed(3); return false; };
        shackleSelectable4.OnInteract += delegate () { ShacklesGetsPressed(4); return false; };
        heartSelectable.OnInteract += delegate () { HeartGetsPressed(); return false; };


        MonographyPlatformsStartingTransforms = new Vector3[5];
        for (int i = 0; i < 5; i ++)
        {
            MonographyPlatformsStartingTransforms[i] = monographyPlatforms[i].transform.localPosition;
            monographyPlatforms[i].transform.localScale = Vector3.zero;
            monographyPlatforms[i].SetActive(false);
        }
    }



    // Puzzle Initialization
    void Start()
    {
        CustomLog("Initializing module");

        ruleseedRandom = ruleseedManager.GetRNG();
        ruleseedSeed = ruleseedRandom.Seed;

        InitializePuzzle();

        haltGearRotationDueToStrike = false;

    }



    void Update()
    {
        RotateShackles();

        RotateGears();

        AnimateTail();

        AnimateWings();

        AnimateMonographyPlatforms();
    }


    void CustomLog(string message, params object[] args)
    {
        Debug.LogFormat("[Scorching Alchemist #{0}] " + (string.Format(message, args)), moduleId);
    }

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Button Pressing Functions
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void ShacklesGetsPressed(int _shackleIndex)
    {
        if (currentModuleState != moduleState.Shackles) { return; }


        heartSelectable.AddInteractionPunch();

        // Is the correct Shackle pressed?
        if ( CharToInt(finalSword.shacklesOrder[currentNumberOfShacklesPressed]) == _shackleIndex)
        {
            CustomLog("Shackles number {0} got pressed. That is correct.", _shackleIndex);

            // Break Shackle Visually
            shackles[_shackleIndex - 1].SetActive(false);


            // Increment index to be pressed
            currentNumberOfShacklesPressed++;

            // Visual & Audio Feedback
            moduleAudio.PlaySoundAtTransform(ShackleSound.name, transform);


            // Did we press all four of them?
            if (currentNumberOfShacklesPressed == 4)
            {
                CustomLog("All four shackles have been destroyed. The shield is down, attack the heart!");
                currentModuleState = moduleState.Heart;
                shield.SetActive(false);

                StartCoroutine(TransitionToHeartState());
            }
        }
        else
        {
            CustomLog("!i! STRIKE !i! Shackles number {0} got pressed. That is incorrect. !i! STRIKE !i!", _shackleIndex);
            thisModule.HandleStrike();
            StartCoroutine(GearStrikeHalt());
        }
    }

    void HeartGetsPressed()
    {
        if (currentModuleState != moduleState.Heart) { return; }
        CustomLog("Heart got pressed.");

        currentNumberOfHeartPresses++;

        // Visual & Audio Feedback
        heartSelectable.AddInteractionPunch();
        moduleAudio.PlaySoundAtTransform(HeartPressSound.name, transform);

        // Reject the previous cooldown
        if (heartCooldownCoroutine != null)
        {
            StopCoroutine(heartCooldownCoroutine);
        }

        // Start new cooldown
        heartCooldownCoroutine = StartCoroutine(HeartCooldown());
    }

    IEnumerator HeartCooldown()
    {
        yield return new WaitForSeconds(2.0f);

        if (currentNumberOfHeartPresses == targetNumberOfPressesOnHeart)
        {
            CustomLog("You pressed the heart {0} times, which is correct. The Scorching Alchemist has been slayed! Congratulations!", currentNumberOfHeartPresses);
            StartCoroutine(TransitionToSolvedState());
            currentModuleState = moduleState.Solved;

            moduleAudio.PlaySoundAtTransform(SolveSound.name, transform);

            thisModule.HandlePass();
        }
        else
        {
            CustomLog("You pressed the heart {0} times, which is incorrect. Expected {1}", currentNumberOfHeartPresses, targetNumberOfPressesOnHeart);

            thisModule.HandleStrike();
            currentNumberOfHeartPresses = 0;
        }

        yield return null;
    }

    IEnumerator GearStrikeHalt()
    {
        haltGearRotationDueToStrike = true;

        yield return new WaitForSeconds(.8f);
        haltGearRotationDueToStrike = false;

    }

    /// <summary> Converts a character into an Int. For String, try int.Parse(string) </summary>
    protected int CharToInt(char _char)
    {
        // Converting a char to an int apparently requires you to subtract a char that happens to represent a number
        // Not add, specifically subtract
        return _char - '0';
    }

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Puzzle Functions
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void InitializePuzzle()
    {
        // Phase 1 : Shield

        ApplyRuleSeed();

        DetermineTailRotation();

        DetermineGearRotations();

        DetermineStartingSword();

        DetermineFinishingSword();





        // Phase 2 : Heart
        SummonMonographyPlatforms();

        DetermineTargetHeartPresses();
    }

    void ApplyRuleSeed()
    {
        // Randomize Shackle Order depending on Ruleseed

        // Default (ruleseed 1) should not change because some values have been hand-picked
        // Like Zeo Sychros being 4321 as it's the ultimate Sword, Desert Seeker being 1234 since it's the main sword you get,
        if (ruleseedSeed == 1)
        { return; }


        CustomLog("Detected Rule Seed {0}. Shuffling the Shackle Sequences.", ruleseedSeed);

        // Ruleseed javascript uses the Fisher-Yates algorithm to shuffle arrays
        // So for ease of everything we're gonna copy that


        // First, save all shackle orders in a separate array
        string[] _shackleOrders = new string[24];

        for (int i = 0; i < 24; i++)
        {
            _shackleOrders[i] = allSwords[i].shacklesOrder;
        }

        // Then, shuffle it using Fisher-Yates
        // Step through the Array in reverse
        int _i = 24;
        int _index;
        string _value;
        while (_i > 1)
        {
            // Get an Index from ruleseed, within [0, _i[
            _index = ruleseedRandom.Next(0, _i);
            _i--;

            // Get the value from that Index
            _value = _shackleOrders[_index];
            // Replace value at that index by the last value (we're stepping through)
            _shackleOrders[_index] = _shackleOrders[_i];
            // Replace the last value by that Index
            _shackleOrders[_i] = _value;
        }


        // Then, re-apply those Shackle Orders to the Swords
        Sword _swordToReplace;
        for (int i = 0; i < 24; i++)
        {
            _swordToReplace = allSwords[i];
            _swordToReplace.shacklesOrder = _shackleOrders[i];
            allSwords[i] = _swordToReplace;

            CustomLog("Sword {0} received Shackle Order {1}", _swordToReplace.name, _swordToReplace.shacklesOrder);
        }
    }

    void DetermineTailRotation()
    {
        // Determine Tail Orientation
        isTailHorizontal = UnityEngine.Random.value > 0.5;
        CustomLog("Tail orientation is {0}. This means movement in the WEAPONS table goes {1}.",
            isTailHorizontal ? "Horizontal" : "Vertical", isTailHorizontal ? "Down" : "Up");

        // Apply Tail Orientation
        tailEndRotation = new Vector3(-60f, 0f, isTailHorizontal ? 0f : 90f);
    }

    void DetermineGearRotations()
    {
        // Determine Gear Turning Orientations
        isLeftGearTurningUp = UnityEngine.Random.value > 0.5;
        isRightGearTurningUp = UnityEngine.Random.value > 0.5;
        
        
        // Determine Gear Movement Factor
        if (isLeftGearTurningUp && isRightGearTurningUp)
        {
            gearRotationFactor = 8;
        }
        else if (isLeftGearTurningUp || isRightGearTurningUp)
        {
            gearRotationFactor = 4;
        }
        else
        {
            gearRotationFactor = 1;
        }


        CustomLog("Left Gear rotates {0} and Right Gear rotates {1}. This means movement in the WEAPONS table is scaled by {2}.",
            isLeftGearTurningUp ? "Up" : "Down", isRightGearTurningUp ? "Up" : "Down", gearRotationFactor);
    }

    void DetermineStartingSword()
    {
        // Determine Starting Sword
        indexInAllSwordsTable = UnityEngine.Random.Range(0, 24);
        startingSword = allSwords[indexInAllSwordsTable];

        // Apply it
        var _material = swordSprite.material;
        _material.SetTexture("_MainTex", startingSword.texture);
        swordSprite.material = _material;
        

        startingSwordName = startingSword.name;
        CustomLog("The Sword laying down on the ground is {0}, left down by a poor previous adventurer.", startingSwordName);
    }

    void DetermineFinishingSword()
    {
        // Get Port Count
        int _numberOfPorts = bombInfo.GetPortCount();
        if (_numberOfPorts == 0)
        {
            _numberOfPorts = 1;
            CustomLog("There are 0 ports on the bomb, however it will be treated as 1.");
        }
        else
        {
            CustomLog("There are {0} ports on the bomb", _numberOfPorts);
        }

        // Get signed total movement
        int _totalMovement = _numberOfPorts * (isTailHorizontal ? 1 : -1) * gearRotationFactor;
        CustomLog("Movement in the WEAPONS Table will be of {0} steps {1}.", Mathf.Abs(_totalMovement), isTailHorizontal ? "Down" : "Up");

        // Move in the table
        indexInAllSwordsTable += _totalMovement;
        indexInAllSwordsTable = Mod(indexInAllSwordsTable, 24);
        finalSword = allSwords[indexInAllSwordsTable];
        CustomLog("Obtained Sword is {0}, with a Shackles Sequence of {1}", finalSword.name, finalSword.shacklesOrder);

        currentNumberOfShacklesPressed = 0;
    }

    void SummonMonographyPlatforms()
    {
        // Summon Monography Platforms
        numberOfMonographyPlatforms = UnityEngine.Random.Range(3, 6);
        CustomLog("A total of {0} Monography Platforms will spawn.", numberOfMonographyPlatforms);

        for (int i = 0; i < numberOfMonographyPlatforms; i++)
        {
            monographyPlatforms[i].SetActive(true);
        }
    }

    void DetermineTargetHeartPresses()
    {
        // Get Serial Number
        string serialNumber = bombInfo.GetSerialNumber();
        // Cross-reference with Sword Name
        int _numberOfMatches = finalSword.name.ToUpperInvariant().Where(x => serialNumber.Contains(x)).ToArray().Length;
        CustomLog("Sword {1} has letters in common with Serial Number {0} times.", _numberOfMatches, finalSword.name);

        targetNumberOfPressesOnHeart = _numberOfMatches * numberOfMonographyPlatforms;
        targetNumberOfPressesOnHeart = Mod(targetNumberOfPressesOnHeart, 5) + 2;
        CustomLog("Press the Heart a total number of {0} times to solve the module.", targetNumberOfPressesOnHeart);
    }

    int Mod(int x, int y)
    {
        // Actual Modulo formula since % is the Remainder Operator... Which can still return a negative value!!
        // Credit to https://discussions.unity.com/t/why-doesnt-the-modulus-operator-have-a-mathematical-notation/517366/10

        // The impportant part is to do the division as Floats, not as ints!!
        return (int)(x - (y * Mathf.Floor((float)x / (float)y)));
    }

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Visuals & Feedbacks Functions
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void RotateShackles()
    {

        // Shackles don't exist outside of Shackles State
        if (currentModuleState != moduleState.Shackles) { return; }

        // Compute Rotation Offset for the last frame
        Vector3 _rotationOffset = new Vector3(0, Time.deltaTime * shacklesRotationSpeed, 0);

        // Apply Rotation Offset
        // Shackles with number 1 and 4 rotate in the same direction, 2 and 3 in the other
        shackles[0].transform.localEulerAngles -= _rotationOffset;
        shackles[1].transform.localEulerAngles += _rotationOffset;
        shackles[2].transform.localEulerAngles += _rotationOffset;
        shackles[3].transform.localEulerAngles -= _rotationOffset;
    }

    void RotateGears()
    {
        // Halt Rotation on strike
        if (haltGearRotationDueToStrike) { return; }

        // Compute Rotation Offset for the last frame
        Vector3 _rotationThisFrame = new Vector3(Time.time * gearRotationSpeed, 0, 90);

        // Apply Rotation Offset
        gearLeft.transform.localEulerAngles = (isLeftGearTurningUp ? 1 : -1) * _rotationThisFrame;
        gearRight.transform.localEulerAngles = (isRightGearTurningUp ? 1 : -1) * _rotationThisFrame;
    }

    void AnimateTail()
    {
        float _rotationSin = Mathf.Sin(Time.time * tailVerticalRotationFrequency);
        float _rotationCos = Mathf.Cos(Time.time * tailHorizontalRotationFrequency);
        // Square the sin to make it stay longer on the extremes
        // Also use Mathf.Abs to keep the sign, as we want it to move up and down both
        Vector3 _rotationOffset = new Vector3(_rotationSin * Mathf.Abs(_rotationSin) * tailVerticalRotationAmplitude, 0, _rotationCos * Mathf.Abs(_rotationCos) * tailHorizontalRotationAmplitude);

        tailSegmentFirst.transform.localEulerAngles = tailSegmentFirstRotation + _rotationOffset;
        tailSegmentSecond.transform.localEulerAngles = tailSegmentMiddleRotation + _rotationOffset;
        tailSegmentThird.transform.localEulerAngles = tailSegmentMiddleRotation + _rotationOffset;
        tailEnd.transform.localEulerAngles = tailEndRotation + _rotationOffset;
    }

    void AnimateWings()
    {
        float _rotationSin = Mathf.Sin(Time.time * wingsRotationFrequency);
        Vector3 _rotationOffset = new Vector3(0, _rotationSin * wingsRotationAmplitude, 0);

        wingsLeft.transform.localEulerAngles = wingsLeftStartingRotation - _rotationOffset;
        wingsRight.transform.localEulerAngles = wingsRightStartingRotation + _rotationOffset;
    }

    void AnimateMonographyPlatforms()
    {
        if (currentModuleState != moduleState.Heart) { return; }

        for (int i = 0;  i < numberOfMonographyPlatforms; i++)
        {
            monographyPlatforms[i].transform.localPosition = MonographyPlatformsStartingTransforms[i] + new Vector3(0, Mathf.Sin(Time.time * 50f * MonographyPlatformsStartingTransforms[i].x) * 0.003f, 0);
        }
    }

    IEnumerator TransitionToHeartState()
    {
        Vector3 startingScale = Vector3.zero;
        Vector3 endingScale = Vector3.one * 1.5f;

        float timer = 0;
        float remappedTimer;

        yield return new WaitForSeconds(0.8f);

        moduleAudio.PlaySoundAtTransform(MonographyPlatformsSound.name, transform);

        while (timer < 1)
        {
            timer += Time.deltaTime;

            remappedTimer = Easing.OutCubic(timer, 0, 1, 1);

            for (int i = 0; i < numberOfMonographyPlatforms; i ++)
            {
                monographyPlatforms[i].transform.localScale = Vector3.Lerp(startingScale, endingScale, remappedTimer);
            }

            // Hide Starting Sword Sprite for Souvenir
            swordSprite.transform.localPosition += Vector3.down * 0.003f * Time.deltaTime;

            yield return null;
        }

        yield return null;
    }

    IEnumerator TransitionToSolvedState()
    {
        Vector3 startingHeartLocation = heart.transform.localPosition;
        Vector3 endingHeartLocation = new Vector3(startingHeartLocation.x, 0.021f, startingHeartLocation.z);

        Vector3 startingHeartScale = heart.transform.localScale;
        Vector3 endingHeartScale = new Vector3(startingHeartScale.x, 0.05f, startingHeartScale.z);

        float timer = 0;
        float remappedTimer;

        while (timer < 1)
        {
            timer += Time.deltaTime * heartDeflateSpeed;

            remappedTimer = Easing.OutCubic(timer, 0, 1, 1);

            heart.transform.localPosition = Vector3.Lerp(startingHeartLocation, endingHeartLocation, remappedTimer);
            heart.transform.localScale = Vector3.Lerp(startingHeartScale, endingHeartScale, remappedTimer);


            // Hide Platforms at the end
            for (int i = 0; i < numberOfMonographyPlatforms; i++)
            {
                monographyPlatforms[i].transform.localScale = Vector3.Lerp(Vector3.one * 1.5f, Vector3.zero, remappedTimer);
            }

            yield return null;
        }

        // Hide Platforms
        foreach (GameObject _platform in monographyPlatforms)
        {
            _platform.SetActive(false);
        }

        yield return null;
    }


    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Editor Serialization Workaround
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    #region Serialization

    /*
        Long Story Short: https://discord.com/channels/160061833166716928/201105291830493193/1294727143770689682
        Structs don't get bundled properly if they are populated in the Inspector through Serialization
        To bypass that, we have to do a workaround with its Serialization
        Basically, before Unity does its stuff we save all the data, and then we put the data back where it should be
    */

    // Lists that'll hold the values
    [SerializeField, HideInInspector] private List<string> serializationSwordNames = new List<string>();
    [SerializeField, HideInInspector] private List<Texture> serializationSwordTextures = new List<Texture>();
    [SerializeField, HideInInspector] private List<string> serializationSwordShackleOrder = new List<string>();

    // Code credit from Qkrisi in his KM Delegate Editor plug-in
    public void OnBeforeSerialize()
    {
        // Save the Data
        serializationSwordNames.Clear();
        serializationSwordTextures.Clear();
        serializationSwordShackleOrder.Clear();
        foreach (Sword _sword in allSwords)
        {
            serializationSwordNames.Add(_sword.name);
            serializationSwordTextures.Add(_sword.texture);
            serializationSwordShackleOrder.Add(_sword.shacklesOrder);
        }
    }

    public void OnAfterDeserialize()
    {
        #if !UNITY_EDITOR   //We only need to recreate the Sword objects in the game
        allSwords.Clear();

        // Repopulate the Data
        var i = -1;
        while (++i > -1)
        {
            try
            {
                allSwords.Add(new Sword
                {
                    name = serializationSwordNames[i],
                    texture = serializationSwordTextures[i],
                    shacklesOrder = serializationSwordShackleOrder[i]
                });
            }
            catch (ArgumentOutOfRangeException)
            { break; }
        }


        // Memory Optimization
        serializationSwordNames.Clear();
        serializationSwordTextures.Clear();
        serializationSwordShackleOrder.Clear();
        #endif
    }

    #endregion


    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Twitch Plays
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=


#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"“!{0} Shackles 1342” to press Shackles in this order. “!{0} Heart 4” to hit the Heart this many times";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        // Credit to Royal_Flu$h for this line 
        var commandParts = command.ToLowerInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        if (commandParts.Length != 2)
        {
            yield return "sendtochat {0} Received command with incorrect number of parts. Please send “!{0} Shackles 1342” or “!{0} Heart 4”.";
            yield break;
        }

        if (commandParts[0] != "shackles" && commandParts[0] != "shackle" && commandParts[0] != "heart" && commandParts[0] != "s" && commandParts[0] != "h")
        {
            yield return "sendtochat {0} Unknown command identifier. Please use “!{0} Shackles” or “!{0} Heart” or “!{0} s” or “!{0} h”.";
            yield break;
        }



        if (commandParts[0] == "shackles" || commandParts[0] == "shackle" || commandParts[0] == "s")
        {

            foreach(char _toPress in commandParts[1])
            {
                yield return new WaitForSeconds(0.1f);

                switch (CharToInt(_toPress))
                {
                    case 1:
                        shackleSelectable1.OnInteract();
                        break;

                    case 2:
                        shackleSelectable2.OnInteract();
                        break;

                    case 3:
                        shackleSelectable3.OnInteract();
                        break;

                    case 4:
                        shackleSelectable4.OnInteract();
                        break;

                    default:
                        yield return "sendtochat {0} Trying to press an unknown shackle. Only number between 1 and 4 are accepted.";
                        yield break;
                }
            }

        }
        else if (commandParts[0] == "heart" || commandParts[0] == "h")
        {
            if (commandParts[1].Length != 1)
            {
                yield return "sendtochat {0} Trying to press an unknown amount of times. Requires presses will only be between 2 and 6.";
                yield break;
            }

            if (CharToInt(commandParts[1][0]) < 2 || CharToInt(commandParts[1][0]) > 6)
            {
                yield return "sendtochat {0} Trying to press an unknown amount of times. Requires presses will only be between 2 and 6.";
                yield break;
            }

            for (int i = 0; i < CharToInt(commandParts[1][0]); i++)
            {
                yield return new WaitForSeconds(0.1f);
                heartSelectable.OnInteract();
            }
        }


        yield break;
    }


    // Auto-solve if Twitch Plays needs to force a solve
    IEnumerator TwitchHandleForcedSolve()
    {
        CustomLog("Received ForceSolve via Twitch Command. Auto-solving Module");

        // Try to hit Shackles
        if (currentModuleState == moduleState.Shackles)
        {
            for (int i = currentNumberOfShacklesPressed; i < 4; i ++)
            {
                switch (CharToInt(finalSword.shacklesOrder[currentNumberOfShacklesPressed]))
                {
                    case 1:
                        shackleSelectable1.OnInteract();
                        break;

                    case 2:
                        shackleSelectable2.OnInteract();
                        break;

                    case 3:
                        shackleSelectable3.OnInteract();
                        break;

                    case 4:
                        shackleSelectable4.OnInteract();
                        break;
                }

                yield return new WaitForSeconds(0.1f);
            }
        }



        // Then hit Heart

        // Prevent strikes by Force Solving mid-solve
        if (heartCooldownCoroutine != null)
        {
            StopCoroutine(heartCooldownCoroutine);
        }
        currentNumberOfHeartPresses = 0;

        for (int i = 0; i < targetNumberOfPressesOnHeart; i++)
        {
            yield return new WaitForSeconds(0.1f);
            heartSelectable.OnInteract();
        }


        yield return null;
    }

}
