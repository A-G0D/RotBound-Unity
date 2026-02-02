using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Movement : MonoBehaviour
{
    public Rigidbody2D player;
    bool isClicked;
    // Start is called before the first frame update
    void Start()
    {
        player = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetAxis("Fire1") == 0 && isClicked)
        {
            isClicked = false;
        }
        if(Input.GetAxis("Fire1") > 0 && !isClicked)
        {
            isClicked = true;
            Vector2 mousePosition = Input.mousePosition;
            Vector2 mouseDirection = mousePosition - new Vector2(Screen.width / 2, Screen.height / 2);
            player.velocity /= 5;
            player.AddForce(mouseDirection.normalized * -800);
        }
        if (Input.GetKey(KeyCode.LeftShift))
        {
            player.drag = 10f;
        } else
        {
            player.drag = 0.4f;
        }
    }
}
